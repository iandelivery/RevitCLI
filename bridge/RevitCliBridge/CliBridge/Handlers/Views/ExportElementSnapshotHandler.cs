using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitCliBridge.Abstractions;

namespace RevitCliBridge.Handlers.Views
{
    public class ExportElementSnapshotHandler : DocumentCommandBase
    {
        public override string CommandName => "export_element_snapshot";
        public override string Description => "Exports an image of the given elements (or the current selection) by temporarily isolating them, tightening the section box on 3D views, and restoring the view state afterwards";
        public override string Category => "Export";

        public override CommandParamSchema[] Parameters => new[]
        {
            new CommandParamSchema { Name = "element_ids", Type = "int[]", Required = false, Description = "Element IDs to snapshot (defaults to the current selection)" },
            new CommandParamSchema { Name = "view_id", Type = "int", Required = false, Description = "View element ID (defaults to the active view)" },
            new CommandParamSchema { Name = "output_path", Type = "string", Required = false, Description = "Output file path (defaults to a timestamped file in the temp directory)" },
            new CommandParamSchema { Name = "file_type", Type = "string", Required = false, Description = "Image format", EnumValues = new[] { "png", "bmp", "jpeg", "tiff", "targa" }, Default = "png" },
            new CommandParamSchema { Name = "dpi", Type = "string", Required = false, Description = "DPI resolution", EnumValues = new[] { "72", "150", "300", "600" }, Default = "300" },
            new CommandParamSchema { Name = "resolution", Type = "int", Required = false, Description = "Pixel resolution for fit-to-page mode", Default = 2000 },
            new CommandParamSchema { Name = "with_section_box", Type = "bool", Required = false, Description = "Tighten the section box around the elements on non-perspective 3D views", Default = true }
        };

        public override string[] Examples => new[]
        {
            "{ \"command\": \"export_element_snapshot\", \"parameters\": {} }",
            "{ \"command\": \"export_element_snapshot\", \"parameters\": { \"element_ids\": [123, 456] } }",
            "{ \"command\": \"export_element_snapshot\", \"parameters\": { \"element_ids\": [123], \"view_id\": 789, \"output_path\": \"C:\\\\output\\\\snapshot.png\" } }"
        };

        protected override string Execute(UIApplication app, Document doc, Dictionary<string, object> parameters, QueuedCommand cmd)
        {
            var uiDoc = app.ActiveUIDocument;
            if (uiDoc is null)
                return CommandResponse.Error(cmd.TaskId, "No active UI document.").ToJson();

            // Resolve target elements: explicit IDs or the current selection.
            var elementIds = HandlerUtilities.GetIntArrayOrNull(parameters, "element_ids");
            var targetIds = new List<ElementId>();

            if (elementIds is { Length: > 0 })
            {
                foreach (var id in elementIds)
                {
                    var element = doc.GetElement(new ElementId(id));
                    if (element is not null)
                        targetIds.Add(element.Id);
                }

                if (targetIds.Count == 0)
                    return CommandResponse.Error(cmd.TaskId, "No valid elements found for the given element_ids.").ToJson();
            }
            else
            {
                targetIds = uiDoc.Selection.GetElementIds().ToList();
                if (targetIds.Count == 0)
                    return CommandResponse.Error(cmd.TaskId, "No element_ids provided and the current selection is empty.").ToJson();
            }

            // Resolve target view.
            Autodesk.Revit.DB.View targetView;
            int? viewId = HandlerUtilities.GetIntOrNull(parameters, "view_id");
            if (viewId.HasValue)
            {
                targetView = doc.GetElement(new ElementId(viewId.Value)) as Autodesk.Revit.DB.View;
                if (targetView is null)
                    return CommandResponse.Error(cmd.TaskId, $"Element with ID {viewId.Value} is not a view.").ToJson();
                if (targetView.IsTemplate)
                    return CommandResponse.Error(cmd.TaskId, "The specified view is a view template.").ToJson();
            }
            else
            {
                targetView = doc.ActiveView;
                if (targetView is null)
                    return CommandResponse.Error(cmd.TaskId, "No active view.").ToJson();
            }

            // Export options.
            string fileTypeStr = HandlerUtilities.GetStringOrNull(parameters, "file_type") ?? "png";
            string dpiStr = HandlerUtilities.GetStringOrNull(parameters, "dpi") ?? "300";
            int? resolution = HandlerUtilities.GetIntOrNull(parameters, "resolution") ?? 2000;

            bool withSectionBox = true;
            if (parameters.TryGetValue("with_section_box", out var wsbObj))
            {
                try { withSectionBox = Convert.ToBoolean(wsbObj); } catch { }
            }

            var imageFileType = ParseImageFileType(fileTypeStr);
            var dpi = dpiStr switch
            {
                "72" => ImageResolution.DPI_72,
                "150" => ImageResolution.DPI_150,
                "600" => ImageResolution.DPI_600,
                _ => ImageResolution.DPI_300
            };

            string outputPath = HandlerUtilities.GetStringOrNull(parameters, "output_path")
                ?? Path.Combine(Path.GetTempPath(), $"revit_snapshot_{DateTime.Now:yyyyMMdd_HHmmss}.{fileTypeStr.ToLowerInvariant()}");
            string directory = Path.GetDirectoryName(outputPath) ?? Path.GetTempPath();
            if (!Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            // Section box applies only to non-perspective 3D views.
            var view3D = targetView as View3D;
            bool useSectionBox = withSectionBox && view3D is { IsPerspective: false };
            BoundingBoxXYZ? oldBox = null;
            bool wasActive = false;
            BoundingBoxXYZ? appliedBounds = null;

            if (targetView is ViewSheet)
                return CommandResponse.Error(cmd.TaskId, "Snapshot export is not supported on sheet views.").ToJson();

            // Compute the section box bounds up front (read-only) so a failure
            // here does not leave any view state behind.
            if (useSectionBox)
            {
                var bounds = SectionBoxUtilities.ComputeElementsBounds(
                    doc, targetIds.Select(i => i.IntegerValue).ToArray(), cmd, out var computeError);
                if (bounds is null)
                    return computeError!;
                appliedBounds = bounds;
            }

            // ExportImage renders from the view's committed visibility state
            // only; temporary hide/isolate is ignored by the print pipeline.
            // So we commit a permanent hide of every other element in the
            // view, export, then restore everything in a second transaction.
            var targetIdSet = new HashSet<ElementId>(targetIds);
            var others = new FilteredElementCollector(doc, targetView.Id)
                .ToElementIds()
                .Where(id => !targetIdSet.Contains(id))
                .ToList();

            var previousActiveView = doc.ActiveView;
            bool switchedView = false;
            string? exportError = null;
            var hiddenIds = new List<ElementId>();

            try
            {
                // Bulk hide in one committed transaction. Revit can defer the
                // "cannot be hidden" validation to Commit, so the commit itself
                // must be inside the try; on failure the transaction rolls back.
                bool bulkOk = false;
                using (var t = new Transaction(doc, "CLI Element Snapshot - Isolate"))
                {
                    t.Start();
                    t.ConfigureFailureHandling();

                    try
                    {
                        if (others.Count > 0)
                            targetView.HideElements(others);

                        if (useSectionBox)
                        {
                            oldBox = view3D!.GetSectionBox();
                            wasActive = view3D.IsSectionBoxActive;

                            view3D.SetSectionBox(appliedBounds);
                            view3D.IsSectionBoxActive = true;
                        }

                        t.Commit();
                        bulkOk = true;
                        hiddenIds.AddRange(others);
                    }
                    catch
                    {
                        // Transaction rolls back; fall back below.
                    }
                }

                if (!bulkOk)
                {
                    // Per-element fallback: hide each element in its own
                    // committed transaction and skip the ones Revit refuses.
                    foreach (var id in others)
                    {
                        using (var t = new Transaction(doc, "CLI Element Snapshot - Isolate One"))
                        {
                            t.Start();
                            t.ConfigureFailureHandling();
                            try
                            {
                                targetView.HideElements(new[] { id });
                                t.Commit();
                                hiddenIds.Add(id);
                            }
                            catch { /* rolls back on dispose */ }
                        }
                    }

                    if (hiddenIds.Count == 0 && others.Count > 0)
                        return CommandResponse.Error(cmd.TaskId, "None of the other view elements could be hidden for isolation.").ToJson();

                    if (useSectionBox)
                    {
                        using (var t = new Transaction(doc, "CLI Element Snapshot - Section Box"))
                        {
                            t.Start();
                            t.ConfigureFailureHandling();

                            oldBox = view3D!.GetSectionBox();
                            wasActive = view3D.IsSectionBoxActive;

                            view3D.SetSectionBox(appliedBounds);
                            view3D.IsSectionBoxActive = true;

                            t.Commit();
                        }
                    }
                }

                // ExportImage renders the current active view, so switch if needed.
                if (previousActiveView is null || !previousActiveView.Id.Equals(targetView.Id))
                {
                    uiDoc.ActiveView = targetView;
                    switchedView = true;
                }

                // ExportImage can create a temporary view inside the document,
                // so it must run within an open transaction (same as export_view).
                using (var tExport = new Transaction(doc, "CLI Element Snapshot - Export"))
                {
                    tExport.Start();
                    tExport.ConfigureFailureHandling();

                    doc.Regenerate();

                    var options = new ImageExportOptions
                    {
                        ExportRange = ExportRange.CurrentView,
                        FilePath = outputPath,
                        FitDirection = FitDirectionType.Horizontal,
                        ImageResolution = dpi,
                        HLRandWFViewsFileType = imageFileType,
                        ShadowViewsFileType = imageFileType,
                        ViewName = targetView.Name,
                        ZoomType = ZoomFitType.FitToPage,
                        Zoom = 100
                    };
                    options.PixelSize = resolution.Value;

                    doc.ExportImage(options);

                    tExport.Commit();
                }
            }
            catch (Exception ex)
            {
                exportError = $"Failed to export element snapshot: {ex.Message}";
            }
            finally
            {
                // Always restore the committed view state, even on failure.
                try
                {
                    if (hiddenIds.Count > 0)
                    {
                        bool restored = false;
                        using (var t = new Transaction(doc, "CLI Element Snapshot - Restore"))
                        {
                            t.Start();
                            t.ConfigureFailureHandling();
                            try
                            {
                                targetView.UnhideElements(hiddenIds);
                                t.Commit();
                                restored = true;
                            }
                            catch { /* rolls back; fall back below */ }
                        }

                        if (!restored)
                        {
                            foreach (var id in hiddenIds)
                            {
                                using (var t = new Transaction(doc, "CLI Element Snapshot - Restore One"))
                                {
                                    t.Start();
                                    t.ConfigureFailureHandling();
                                    try
                                    {
                                        targetView.UnhideElements(new[] { id });
                                        t.Commit();
                                    }
                                    catch { /* rolls back on dispose */ }
                                }
                            }
                        }
                    }

                    if (useSectionBox && oldBox is not null)
                    {
                        using (var t = new Transaction(doc, "CLI Element Snapshot - Restore Box"))
                        {
                            t.Start();
                            t.ConfigureFailureHandling();

                            view3D!.SetSectionBox(oldBox);
                            view3D.IsSectionBoxActive = wasActive;

                            t.Commit();
                        }
                    }
                }
                catch { /* best-effort restore */ }

                if (switchedView && previousActiveView is not null)
                    uiDoc.ActiveView = previousActiveView;
            }

            if (exportError is not null)
                return CommandResponse.Error(cmd.TaskId, exportError).ToJson();

            var fileInfo = new FileInfo(outputPath);
            var result = new
            {
                view_id = targetView.Id.IntegerValue,
                view_name = targetView.Name,
                output_path = outputPath,
                file_size = fileInfo.Exists ? fileInfo.Length : 0,
                isolated_count = targetIds.Count,
                section_box = appliedBounds is null
                    ? null
                    : new
                    {
                        min_x = appliedBounds.Min.X.FeetToMillimeter(),
                        min_y = appliedBounds.Min.Y.FeetToMillimeter(),
                        min_z = appliedBounds.Min.Z.FeetToMillimeter(),
                        max_x = appliedBounds.Max.X.FeetToMillimeter(),
                        max_y = appliedBounds.Max.Y.FeetToMillimeter(),
                        max_z = appliedBounds.Max.Z.FeetToMillimeter()
                    }
            };

            return CommandResponse.Success(cmd.TaskId, result, "Element snapshot exported successfully.").ToJson();
        }

        private static ImageFileType ParseImageFileType(string value)
        {
            return value.ToLowerInvariant() switch
            {
                "bmp" => ImageFileType.BMP,
                "jpeg" or "jpg" => ImageFileType.JPEGLossless,
                "targa" or "tga" => ImageFileType.TARGA,
                "tiff" or "tif" => ImageFileType.TIFF,
                _ => ImageFileType.PNG
            };
        }
    }
}

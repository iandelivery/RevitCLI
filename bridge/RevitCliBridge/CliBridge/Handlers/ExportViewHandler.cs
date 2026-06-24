using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.IO;
using RevitCliBridge.Abstractions;
using System.Linq;

namespace RevitCliBridge.Handlers
{
    public class ExportViewHandler : DocumentCommandBase
    {
        public override string CommandName => "export_view";
        public override string Description => "Exports a view to an image file (defaults to the active view)";
        public override string Category => "Export";

        public override CommandParamSchema[] Parameters => new[]
        {
            new CommandParamSchema { Name = "view_name", Type = "string", Required = false, Description = "Name of the view to export (defaults to the active view)" },
            new CommandParamSchema { Name = "output_path", Type = "string", Required = false, Description = "Output file path (defaults to temp directory)" },
            new CommandParamSchema { Name = "file_type", Type = "string", Required = false, Description = "Image format", EnumValues = new[] { "png", "bmp", "jpeg", "tiff", "targa" }, Default = "png" },
            new CommandParamSchema { Name = "dpi", Type = "string", Required = false, Description = "DPI resolution", EnumValues = new[] { "72", "150", "300", "600" }, Default = "300" },
            new CommandParamSchema { Name = "fit_direction", Type = "string", Required = false, Description = "Fit direction", EnumValues = new[] { "horizontal", "vertical" }, Default = "horizontal" },
            new CommandParamSchema { Name = "zoom_type", Type = "string", Required = false, Description = "Zoom type", EnumValues = new[] { "fit", "zoom" }, Default = "fit" },
            new CommandParamSchema { Name = "resolution", Type = "int", Required = false, Description = "Pixel resolution for fit-to-page mode" },
            new CommandParamSchema { Name = "zoom_value", Type = "double", Required = false, Description = "Zoom percentage (for zoom mode)", Default = 100 }
        };

        public override string[] Examples => new[]
        {
            "{ \"command\": \"export_view\", \"parameters\": {} }",
            "{ \"command\": \"export_view\", \"parameters\": { \"view_name\": \"Level 1 - Floor Plan\", \"output_path\": \"C:\\\\output\\\\view.png\" } }",
            "{ \"command\": \"export_view\", \"parameters\": { \"view_name\": \"3D View 1\", \"file_type\": \"jpeg\", \"dpi\": \"300\" } }"
        };

        protected override string Execute(UIApplication app, Document doc, Dictionary<string, object> parameters, QueuedCommand cmd)
        {

            var uiDoc = app.ActiveUIDocument;
            if (uiDoc is null)
                return CommandResponse.Error(cmd.TaskId, "No active UI document.").ToJson();

            var activeView = doc.ActiveView;
            if (activeView is null)
                return CommandResponse.Error(cmd.TaskId, "No active view.").ToJson();

            // Resolve target view by name; fall back to the active view.
            string? viewNameParam = HandlerUtilities.GetStringOrNull(parameters, "view_name");
            Autodesk.Revit.DB.View targetView = activeView;
            if (!string.IsNullOrWhiteSpace(viewNameParam))
            {
                var matchedView = new FilteredElementCollector(doc)
                    .OfClass(typeof(Autodesk.Revit.DB.View))
                    .Cast<Autodesk.Revit.DB.View>()
                    .FirstOrDefault(v => v.Name.Equals(viewNameParam, StringComparison.OrdinalIgnoreCase)
                                         && !v.IsTemplate);
                if (matchedView is null)
                    return CommandResponse.Error(cmd.TaskId,
                        $"View '{viewNameParam}' not found in the document.").ToJson();
                targetView = matchedView;
            }

            string outputPath = HandlerUtilities.GetStringOrNull(parameters, "output_path")
                ?? Path.Combine(Path.GetTempPath(), "revit_export.png");

            string fitDirectionStr = HandlerUtilities.GetStringOrNull(parameters, "fit_direction") ?? "horizontal";
            string zoomTypeStr = HandlerUtilities.GetStringOrNull(parameters, "zoom_type") ?? "fit";
            string dpiStr = HandlerUtilities.GetStringOrNull(parameters, "dpi") ?? "300";
            string fileTypeStr = HandlerUtilities.GetStringOrNull(parameters, "file_type") ?? "png";
            string shadowFileTypeStr = HandlerUtilities.GetStringOrNull(parameters, "shadow_file_type") ?? "png";
            string exportRangeStr = HandlerUtilities.GetStringOrNull(parameters, "export_range") ?? "current_view";

            int? resolution = HandlerUtilities.GetIntOrNull(parameters, "resolution") ?? 2000;
            double zoomValue = HandlerUtilities.GetDoubleOrNull(parameters, "zoom_value") ?? 100.0;

            FitDirectionType fitDirection = fitDirectionStr.Equals("vertical", StringComparison.OrdinalIgnoreCase)
                ? FitDirectionType.Vertical : FitDirectionType.Horizontal;

            ZoomFitType zoomType = zoomTypeStr.Equals("zoom", StringComparison.OrdinalIgnoreCase)
                ? ZoomFitType.Zoom : ZoomFitType.FitToPage;

            ImageResolution dpi = dpiStr switch
            {
                "72" => ImageResolution.DPI_72,
                "150" => ImageResolution.DPI_150,
                "300" => ImageResolution.DPI_300,
                "600" => ImageResolution.DPI_600,
                _ => ImageResolution.DPI_300
            };

            ImageFileType fileType = ParseImageFileType(fileTypeStr);
            ImageFileType shadowFileType = ParseImageFileType(shadowFileTypeStr);

            ExportRange exportRange = exportRangeStr.Equals("visible_region", StringComparison.OrdinalIgnoreCase)
                ? ExportRange.VisibleRegionOfCurrentView
                : ExportRange.CurrentView;

            string directory = Path.GetDirectoryName(outputPath) ?? Path.GetTempPath();
            if (!Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            using (Transaction t = new Transaction(doc, "CLI Export View"))
            {
                t.Start();
                t.ConfigureFailureHandling();

                try
                {
                    var imageExportOptions = new ImageExportOptions
                    {
                        ExportRange = exportRange,
                        FilePath = outputPath,
                        FitDirection = fitDirection,
                        ImageResolution = dpi,
                        HLRandWFViewsFileType = fileType,
                        ShadowViewsFileType = shadowFileType,
                        ViewName = targetView.Name,
                        ZoomType = zoomType,
                        Zoom = (int)zoomValue
                    };

                    if (resolution.HasValue && zoomType == ZoomFitType.FitToPage)
                        imageExportOptions.PixelSize = resolution.Value;

                    // Override the active view for this export.
                    var previousActiveView = doc.ActiveView;
                    if (!targetView.Id.Equals(previousActiveView?.Id))
                        uiDoc.ActiveView = targetView;

                    try
                    {
                        doc.ExportImage(imageExportOptions);
                    }
                    finally
                    {
                        if (previousActiveView is not null && !targetView.Id.Equals(previousActiveView.Id))
                            uiDoc.ActiveView = previousActiveView;
                    }

                    t.Commit();

                    var fileInfo = new FileInfo(outputPath);
                    var result = new
                    {
                        view_name = targetView.Name,
                        view_id = targetView.Id.IntegerValue,
                        output_path = outputPath,
                        file_size = fileInfo.Exists ? fileInfo.Length : 0,
                        fit_direction = fitDirection.ToString(),
                        zoom_type = zoomType.ToString(),
                        dpi = dpi.ToString(),
                        file_type = fileType.ToString(),
                        shadow_file_type = shadowFileType.ToString(),
                        resolution = resolution,
                        export_range = exportRange.ToString()
                    };

                    return CommandResponse.Success(cmd.TaskId, result, "View exported successfully.").ToJson();
                }
                catch (Exception ex)
                {
                    t.RollBack();
                    return CommandResponse.Error(cmd.TaskId,
                        $"Failed to export view: {ex.Message}").ToJson();
                }
            }
        }

        private ImageFileType ParseImageFileType(string value)
        {
            return value.ToLower() switch
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

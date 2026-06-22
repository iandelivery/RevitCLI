using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitCliBridge.Abstractions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RevitCliBridge.Handlers
{
    public class BatchExportHandler : DocumentCommandBase
    {
        public override string CommandName => "batch_export";
        protected override string Execute(UIApplication app, Document doc, Dictionary<string, object> parameters, QueuedCommand cmd)
        {

            string? format = HandlerUtilities.GetStringOrNull(parameters, "format")?.ToLower();
            if (string.IsNullOrEmpty(format))
                return CommandResponse.Error(cmd.TaskId, "Missing 'format' parameter. Supported: pdf, dwg, img.").ToJson();

            string outputDir = HandlerUtilities.GetStringOrNull(parameters, "output_dir")
                ?? Path.GetTempPath();

            if (!Directory.Exists(outputDir))
                Directory.CreateDirectory(outputDir);

            var viewIds = ResolveViewIds(doc, parameters);
            if (viewIds is null || viewIds.Count == 0)
                return CommandResponse.Error(cmd.TaskId, "No views found. Use view_ids, sheet_ids, or all_sheets.").ToJson();

            var results = new List<object>();
            int successCount = 0;
            int failCount = 0;
            int totalViews = viewIds.Count;

            using (Transaction t = new Transaction(doc, "CLI Batch Export"))
            {
                t.Start();
                t.ConfigureFailureHandling();

                try
                {
                    switch (format)
                    {
                        case "pdf":
                            ExportPdf(doc, viewIds, outputDir, parameters, results, ref successCount, ref failCount, cmd.TaskId);
                            break;

                        case "dwg":
                            ExportDwg(doc, viewIds, outputDir, parameters, results, ref successCount, ref failCount, cmd.TaskId);
                            break;

                        case "img":
                        case "image":
                            ExportImages(doc, viewIds, outputDir, parameters, results, ref successCount, ref failCount, cmd.TaskId);
                            break;

                        default:
                            return CommandResponse.Error(cmd.TaskId,
                                $"Unsupported format: '{format}'. Supported: pdf, dwg, img.").ToJson();
                    }
                }
                catch (Exception ex)
                {
                    t.RollBack();
                    return CommandResponse.Error(cmd.TaskId, $"Batch export failed: {ex.Message}").ToJson();
                }

                t.Commit();
            }

            var result = new
            {
                format = format,
                output_dir = outputDir,
                total = viewIds.Count,
                success_count = successCount,
                fail_count = failCount,
                results = results
            };

            return CommandResponse.Success(cmd.TaskId, result,
                $"Batch export ({format}): {successCount} succeeded, {failCount} failed.").ToJson();
        }

        private List<ElementId> ResolveViewIds(Document doc, Dictionary<string, object> parameters)
        {
            var viewIdInts = HandlerUtilities.GetIntArrayOrNull(parameters, "view_ids");
            if (viewIdInts is not null && viewIdInts.Length > 0)
                return viewIdInts.Select(id => new ElementId(id)).ToList();

            var sheetIdInts = HandlerUtilities.GetIntArrayOrNull(parameters, "sheet_ids");
            if (sheetIdInts is not null && sheetIdInts.Length > 0)
            {
                var viewIds = new List<ElementId>();
                foreach (var sid in sheetIdInts)
                {
                    var sheet = doc.GetElement(new ElementId(sid)) as ViewSheet;
                    if (sheet is not null)
                        viewIds.Add(sheet.Id);
                }
                return viewIds;
            }

            bool allSheets = false;
            if (parameters.TryGetValue("all_sheets", out var allVal) && allVal is not null)
                allSheets = Convert.ToBoolean(allVal);

            if (allSheets)
            {
                return new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSheet))
                    .Where(s => !((ViewSheet)s).IsPlaceholder)
                    .Select(s => s.Id)
                    .Take(500)
                    .ToList();
            }

            return new List<ElementId>();
        }

        private void ExportPdf(Document doc, List<ElementId> viewIds, string outputDir,
            Dictionary<string, object> parameters, List<object> results, ref int successCount, ref int failCount, string taskId)
        {
            TaskRegistry.SetProgress(taskId, 10, "Preparing PDF export...");
            string? pdfSetup = HandlerUtilities.GetStringOrNull(parameters, "pdf_setup");

            var views = new List<ElementId>();
            foreach (var vid in viewIds)
            {
                var element = doc.GetElement(vid);
                if (element is ViewSheet || element is View)
                    views.Add(vid);
            }

            if (views.Count == 0)
                return;

            try
            {
                foreach (var vid in views)
                {
                    var element = doc.GetElement(vid);
                    string fileName = element?.Name?.ReplaceInvalidChars() ?? vid.IntegerValue.ToString();
                    string filePath = Path.Combine(outputDir, $"{fileName}.pdf");
                    try
                    {
                        var imgOptions = new ImageExportOptions
                        {
                            ExportRange = ExportRange.CurrentView,
                            FilePath = filePath,
                            FitDirection = FitDirectionType.Horizontal,
                            ImageResolution = ImageResolution.DPI_300,
                            HLRandWFViewsFileType = ImageFileType.PNG,
                            ZoomType = ZoomFitType.FitToPage
                        };
                        doc.ExportImage(imgOptions);
                        successCount++;
                        results.Add(new { view_id = vid.IntegerValue, output_path = filePath, success = true });
                    }
                    catch (Exception ex)
                    {
                        failCount++;
                        results.Add(new { view_id = vid.IntegerValue, success = false, error = ex.Message });
                    }
                }
            }
            catch (Exception ex)
            {
                failCount += views.Count;
                results.Add(new { format = "pdf", success = false, error = ex.Message });
            }
        }

        private void ExportDwg(Document doc, List<ElementId> viewIds, string outputDir,
            Dictionary<string, object> parameters, List<object> results, ref int successCount, ref int failCount, string taskId)
        {
            TaskRegistry.SetProgress(taskId, 10, "Preparing DWG export...");
            string? dwgSetup = HandlerUtilities.GetStringOrNull(parameters, "dwg_setup");

            var views = new List<ElementId>();
            foreach (var vid in viewIds)
            {
                var element = doc.GetElement(vid);
                if (element is ViewSheet || element is View)
                    views.Add(vid);
            }

            if (views.Count == 0)
                return;

            try
            {
                var dwgOptions = new DWGExportOptions();
                if (!string.IsNullOrEmpty(dwgSetup))
                    dwgOptions.SharedCoords = true;

                doc.Export(outputDir, string.Empty, views, dwgOptions);
                TaskRegistry.SetProgress(taskId, 90, "DWG export completed");
                successCount += views.Count;
                results.Add(new { format = "dwg", view_count = views.Count, output_dir = outputDir });
            }
            catch (Exception ex)
            {
                failCount += views.Count;
                results.Add(new { format = "dwg", success = false, error = ex.Message });
            }
        }

        private void ExportImages(Document doc, List<ElementId> viewIds, string outputDir,
            Dictionary<string, object> parameters, List<object> results, ref int successCount, ref int failCount, string taskId)
        {
            int total = viewIds.Count;
            for (int i = 0; i < viewIds.Count; i++)
            {
                var vid = viewIds[i];
                TaskRegistry.SetProgress(taskId,
                    (int)((i + 1) / (double)total * 100),
                    $"Exporting image {i + 1}/{total}");

                var element = doc.GetElement(vid);
                if (element is null) continue;

                string fileName = (element is ViewSheet vs ? vs.SheetNumber : element.Name)
                    .ReplaceInvalidChars();
                string filePath = Path.Combine(outputDir, $"{fileName}.png");

                try
                {
                    var imgOptions = new ImageExportOptions
                    {
                        ExportRange = ExportRange.CurrentView,
                        FilePath = filePath,
                        FitDirection = FitDirectionType.Horizontal,
                        ImageResolution = ImageResolution.DPI_300,
                        HLRandWFViewsFileType = ImageFileType.PNG,
                        ZoomType = ZoomFitType.FitToPage
                    };

                    doc.ExportImage(imgOptions);
                    successCount++;
                    results.Add(new { view_id = vid.IntegerValue, output_path = filePath, success = true });
                }
                catch (Exception ex)
                {
                    failCount++;
                    results.Add(new { view_id = vid.IntegerValue, success = false, error = ex.Message });
                }
            }
        }
    }

    internal static class StringExtensions
    {
        internal static string ReplaceInvalidChars(this string fileName)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            var result = fileName;
            foreach (var c in invalidChars)
                result = result.Replace(c, '_');
            return result;
        }
    }
}

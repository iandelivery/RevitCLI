using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.IO;
using RevitCliBridge.Abstractions;

namespace RevitCliBridge.Handlers
{
    public class ExportViewHandler : DocumentCommandBase
    {
        public override string CommandName => "export_view";
        protected override string Execute(UIApplication app, Document doc, Dictionary<string, object> parameters, QueuedCommand cmd)
        {

            var uiDoc = app.ActiveUIDocument;
            if (uiDoc is null)
                return CommandResponse.Error(cmd.TaskId, "No active UI document.").ToJson();

            var activeView = doc.ActiveView;
            if (activeView is null)
                return CommandResponse.Error(cmd.TaskId, "No active view.").ToJson();

            string outputPath = HandlerUtilities.GetStringOrNull(parameters, "output_path")
                ?? Path.Combine(Path.GetTempPath(), "revit_export.png");

            string fitDirectionStr = HandlerUtilities.GetStringOrNull(parameters, "fit_direction") ?? "horizontal";
            string zoomTypeStr = HandlerUtilities.GetStringOrNull(parameters, "zoom_type") ?? "fit";
            string dpiStr = HandlerUtilities.GetStringOrNull(parameters, "dpi") ?? "300";
            string fileTypeStr = HandlerUtilities.GetStringOrNull(parameters, "file_type") ?? "png";
            string shadowFileTypeStr = HandlerUtilities.GetStringOrNull(parameters, "shadow_file_type") ?? "png";
            string exportRangeStr = HandlerUtilities.GetStringOrNull(parameters, "export_range") ?? "current_view";

            int? resolution = HandlerUtilities.GetIntOrNull(parameters, "resolution");
            double zoomValue = HandlerUtilities.GetDoubleOrNull(parameters, "zoom_value") ?? 100.0;

            FitDirectionType fitDirection = fitDirectionStr.Equals("vertical", StringComparison.OrdinalIgnoreCase)
                ? FitDirectionType.Vertical : FitDirectionType.Horizontal;

            ZoomFitType zoomType = zoomTypeStr.Equals("zoom", StringComparison.OrdinalIgnoreCase)
                ? ZoomFitType.Zoom : ZoomFitType.FitToPage;

            ImageResolution dpi = dpiStr switch
            {
                "72" => ImageResolution.DPI_72,
                "150" => ImageResolution.DPI_150,
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
                        ViewName = activeView.Name,
                        ZoomType = zoomType,
                        Zoom = (int)zoomValue
                    };

                    if (resolution.HasValue && zoomType == ZoomFitType.FitToPage)
                        imageExportOptions.PixelSize = resolution.Value;

                    doc.ExportImage(imageExportOptions);

                    t.Commit();

                    var fileInfo = new FileInfo(outputPath);
                    var result = new
                    {
                        view_name = activeView.Name,
                        view_id = activeView.Id.IntegerValue,
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

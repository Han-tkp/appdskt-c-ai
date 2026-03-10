using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ClosedXML.Excel;

namespace DropDetect.Services;

public class AnalysisReportItem
{
    public string LocationName { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public AnalysisResult Result { get; set; } = new();
    public string? RawImageName { get; set; }
    public string? ProcessedImageName { get; set; }
}

public interface IExcelExportService
{
    Task ExportAsync(IEnumerable<AnalysisReportItem> reportItems, string filePath);
}

public class ExcelExportService : IExcelExportService
{
    public async Task ExportAsync(IEnumerable<AnalysisReportItem> reportItems, string filePath)
    {
        await Task.Run(() =>
        {
            using var workbook = new XLWorkbook();

            // 1. Create Summary Sheet
            CreateSummarySheet(workbook, reportItems);

            // 2. Create Detailed Sheets for each Location
            foreach (var item in reportItems)
            {
                CreateDetailedSheet(workbook, item);
            }

            workbook.SaveAs(filePath);
        });
    }

    private void CreateSummarySheet(XLWorkbook workbook, IEnumerable<AnalysisReportItem> reportItems)
    {
        var ws = workbook.Worksheets.Add("Dashboard สรุปผล");

        // --- Overall Summary Section ---
        ws.Cell(1, 1).Value = "DropDetect - Laboratory Analysis Dashboard";
        var titleRange = ws.Range(1, 1, 1, 7);
        titleRange.Merge().Style.Font.SetBold().Font.SetFontSize(16).Font.SetFontColor(XLColor.White).Fill.SetBackgroundColor(XLColor.DarkBlue);
        titleRange.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

        int totalDrops = reportItems.Sum(x => x.Result.TotalAccumulatedCount);
        int totalSampled = reportItems.Sum(x => x.Result.Count);

        // Calculate Global VMD if applicable (Averaging the VMDs for a quick overall glance)
        double avgVmd = reportItems.Any() ? reportItems.Average(x => x.Result.Dv05_VMD) : 0;
        double avgSpan = reportItems.Any() ? reportItems.Average(x => x.Result.Span) : 0;

        ws.Cell(3, 1).Value = "Total Slides Analyzed:";
        ws.Cell(3, 2).Value = reportItems.Count();
        ws.Cell(4, 1).Value = "Total Droplets Detected (All Slides):";
        ws.Cell(4, 2).Value = totalDrops;
        ws.Cell(5, 1).Value = "Total Droplets Sampled:";
        ws.Cell(5, 2).Value = totalSampled;

        ws.Cell(3, 4).Value = "Overall Average VMD (µm):";
        ws.Cell(3, 5).Value = Math.Round(avgVmd, 2);
        ws.Cell(4, 4).Value = "Overall Average SPAN:";
        ws.Cell(4, 5).Value = Math.Round(avgSpan, 2);

        var summaryBlock = ws.Range(3, 1, 5, 5);
        summaryBlock.Style.Font.SetBold();

        // --- Detailed Table Headers ---
        int tableStartRow = 8;
        ws.Cell(tableStartRow, 1).Value = "Location Name (ชื่อสถานที่)";
        ws.Cell(tableStartRow, 2).Value = "Date (วันที่)";
        ws.Cell(tableStartRow, 3).Value = "Total Detected (เจอทั้งหมด)";
        ws.Cell(tableStartRow, 4).Value = "Sampled Count (จำนวนสุ่ม)";
        ws.Cell(tableStartRow, 5).Value = "VMD (Dv0.5) µm";
        ws.Cell(tableStartRow, 6).Value = "SPAN";
        ws.Cell(tableStartRow, 7).Value = "Evaluation (ผลการประเมิน)";

        // Style Headers
        var headerRange = ws.Range(tableStartRow, 1, tableStartRow, 7);
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Fill.BackgroundColor = XLColor.LightGray;
        headerRange.Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);

        int row = tableStartRow + 1;
        foreach (var item in reportItems)
        {
            ws.Cell(row, 1).Value = item.LocationName;
            ws.Cell(row, 2).Value = item.Timestamp.ToString("yyyy-MM-dd HH:mm:ss");
            ws.Cell(row, 3).Value = item.Result.TotalAccumulatedCount;
            ws.Cell(row, 4).Value = item.Result.Count;
            ws.Cell(row, 5).Value = Math.Round(item.Result.Dv05_VMD, 2);
            ws.Cell(row, 6).Value = Math.Round(item.Result.Span, 2);
            ws.Cell(row, 7).Value = item.Result.IsPassed && item.Result.IsCountSufficient ? "PASS" : "FAIL";

            // Coloring Pass/Fail
            if (item.Result.IsPassed && item.Result.IsCountSufficient)
            {
                ws.Cell(row, 7).Style.Font.FontColor = XLColor.Green;
                ws.Cell(row, 7).Style.Font.Bold = true;
            }
            else
            {
                ws.Cell(row, 7).Style.Font.FontColor = XLColor.Red;
                ws.Cell(row, 7).Style.Font.Bold = true;
            }

            var rowRange = ws.Range(row, 1, row, 7);
            rowRange.Style.Border.SetBottomBorder(XLBorderStyleValues.Thin);
            rowRange.Style.Border.SetBottomBorderColor(XLColor.LightGray);

            row++;
        }

        // Apply Data Bars Conditional Formatting for VMD and SPAN to act as visual charts
        if (row > tableStartRow + 1)
        {
            var vmdRange = ws.Range(tableStartRow + 1, 5, row - 1, 5);
            vmdRange.AddConditionalFormat().DataBar(XLColor.PastelBlue).LowestValue().HighestValue();

            var spanRange = ws.Range(tableStartRow + 1, 6, row - 1, 6);
            spanRange.AddConditionalFormat().DataBar(XLColor.PastelOrange).LowestValue().HighestValue();
        }

        ws.Columns(1, 7).AdjustToContents();
        ws.Column(1).Width = Math.Max(ws.Column(1).Width, 25); // Ensure Location Name has enough space
    }

    private void CreateDetailedSheet(XLWorkbook workbook, AnalysisReportItem item)
    {
        string rawName = string.IsNullOrWhiteSpace(item.LocationName) ? "Unknown" : item.LocationName;
        // 1. Sanitize location name (Remove invalid Excel sheet characters: \ / ? * [ ] : )
        string sanitizedName = System.Text.RegularExpressions.Regex.Replace(rawName, @"[\\/?*[\]:]", "_");
        if (string.IsNullOrWhiteSpace(sanitizedName)) sanitizedName = "Sheet";

        // 2. Sheet name max length is 31, ensure valid name
        string safeSheetName = new string(sanitizedName.Take(25).ToArray());
        int dupCount = 1;

        // Loop until we find a unique name
        string finalSheetName = safeSheetName;
        while (workbook.Worksheets.Contains(finalSheetName))
        {
            finalSheetName = safeSheetName + $"_{dupCount}";
            dupCount++;
        }

        var ws = workbook.Worksheets.Add(finalSheetName);

        // Required headers matching WHO template format
        ws.Cell(1, 1).Value = "ละอองที่"; // Index
        ws.Cell(1, 2).Value = "เส้นผ่านศูนย์กลางที่สำรวจได้\n(2 เท่าของรัศมี)"; // Measured Diameter
        ws.Cell(1, 3).Value = "เส้นผ่านศูนย์กลางจริง\n(หารด้วย 1.15)\n(อ่านค่านี้)"; // True Diameter
        ws.Cell(1, 4).Value = "ปริมาตรที่คำนวณ\n(ปริมาตรทรงกลม = 4/3¶R3)"; // Calculated Volume
        ws.Cell(1, 5).Value = "ผลรวมสะสมของปริมาตรในแต่ละชั้น\n(ปริมาตรสะสม)"; // Cumulative Volume
        ws.Cell(1, 6).Value = "% ของปริมาตร\nในแต่ละชั้น\n(เพื่อหาจุดที่เป็น 50% ของปริมาตรสะสม)"; // % Volume

        var headerRow = ws.Range(1, 1, 1, 6);
        headerRow.Style.Alignment.WrapText = true;
        headerRow.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        headerRow.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        headerRow.Style.Font.Bold = true;
        headerRow.Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);
        headerRow.Style.Border.SetInsideBorder(XLBorderStyleValues.Thin);

        // Map data rows
        int row = 2;
        int index = 1;

        bool highlightedSn50 = false;
        double targetVmd = item.Result.Dv05_VMD;

        var orderedDroplets = item.Result.Droplets.OrderBy(d => d.TrueDiameterUm).ToList();

        foreach (var drop in orderedDroplets)
        {
            ws.Cell(row, 1).Value = index;
            ws.Cell(row, 2).Value = Math.Round(drop.CraterDiameterUm, 4);
            ws.Cell(row, 3).Value = Math.Round(drop.TrueDiameterUm, 3);
            ws.Cell(row, 4).Value = Math.Round(drop.Volume, 3);
            ws.Cell(row, 5).Value = Math.Round(drop.CumulativeVolume, 3);
            ws.Cell(row, 6).Value = Math.Round(drop.CumulativeVolumePct, 2);

            var rowRange = ws.Range(row, 1, row, 6);
            rowRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            rowRange.Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);
            rowRange.Style.Border.SetInsideBorder(XLBorderStyleValues.Thin);

            // Highlight the SN-50 row (where CumulativeVolumePct crosses 50%)
            if (!highlightedSn50 && drop.CumulativeVolumePct >= 50.0)
            {
                rowRange.Style.Fill.BackgroundColor = XLColor.Yellow;
                highlightedSn50 = true;

                // Add SN-50 indicator
                ws.Cell(row, 7).Value = "SN-50";
                ws.Cell(row, 7).Style.Font.Bold = true;
                ws.Cell(row, 7).Style.Fill.BackgroundColor = XLColor.Yellow;
            }

            row++;
            index++;
        }

        ws.Columns(1, 6).AdjustToContents();
        // Give headers sensible widths if they get too wide from AdjustToContents
        for (int c = 1; c <= 6; c++)
        {
            if (ws.Column(c).Width > 25) ws.Column(c).Width = 25;
        }
    }
}

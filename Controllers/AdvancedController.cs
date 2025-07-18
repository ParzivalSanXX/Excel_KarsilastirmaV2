using ExcelComparator.Models;
using ExcelComparator.Services;
using Microsoft.AspNetCore.Mvc;
using OfficeOpenXml;

namespace ExcelComparator.Controllers
{
    public class AdvancedController : Controller
    {
        private readonly ILogger<AdvancedController> _logger;
        private readonly IExcelService _excelService;

        public AdvancedController(ILogger<AdvancedController> logger, IExcelService excelService)
        {
            _logger = logger;
            _excelService = excelService;
        }

        public IActionResult Index()
        {
            var model = new AdvancedAnalysisModel();
            ViewBag.FilterPresets = GetFilterPresets();
            return View(model);
        }

        [HttpPost]
        public async Task<IActionResult> AnalyzeData(AdvancedAnalysisModel model)
        {
            try
            {
                if (model.DataFile == null)
                {
                    ViewBag.Error = "Lütfen bir Excel dosyası seçin.";
                    ViewBag.FilterPresets = GetFilterPresets();
                    return View("Index", model);
                }

                var filterResult = await _excelService.FilterData(model.DataFile, model.Filters);

                if (!filterResult.Success)
                {
                    ViewBag.Error = filterResult.ErrorMessage;
                    ViewBag.FilterPresets = GetFilterPresets();
                    return View("Index", model);
                }

                var analysisResult = new DataAnalysisResult
                {
                    FilterResult = filterResult,
                    ColumnStatistics = GenerateColumnStatistics(filterResult.FilteredData, filterResult.Headers),
                    ValueDistribution = GenerateValueDistribution(filterResult.FilteredData, filterResult.Headers),
                    Insights = GenerateInsights(filterResult)
                };

                // Session'a kaydet (AJAX işlemleri için)
                HttpContext.Session.SetString("AnalysisResult", System.Text.Json.JsonSerializer.Serialize(analysisResult));
                HttpContext.Session.SetString("CurrentFilters", System.Text.Json.JsonSerializer.Serialize(model.Filters));

                ViewBag.AnalysisResult = analysisResult;
                ViewBag.Success = $"Analiz tamamlandı! {filterResult.FilteredRecords}/{filterResult.TotalRecords} kayıt filtrelendi.";
                ViewBag.FilterPresets = GetFilterPresets();

                return View("Index", model);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Veri analizi sırasında hata");
                ViewBag.Error = $"Hata: {ex.Message}";
                ViewBag.FilterPresets = GetFilterPresets();
                return View("Index", model);
            }
        }

        [HttpPost]
        public async Task<IActionResult> GetDataByType(string dataType = "filtered", int limit = 100)
        {
            try
            {
                // Session'dan analiz sonucunu al
                var analysisResultJson = HttpContext.Session.GetString("AnalysisResult");
                if (string.IsNullOrEmpty(analysisResultJson))
                {
                    return Json(new { success = false, message = "Önce analiz yapmalısınız." });
                }

                var analysisResult = System.Text.Json.JsonSerializer.Deserialize<DataAnalysisResult>(analysisResultJson);

                List<Dictionary<string, object>> data;
                string title;
                int totalCount;

                switch (dataType)
                {
                    case "filtered":
                        data = analysisResult.FilterResult.FilteredData.Take(limit).ToList();
                        title = "Filtrelenmiş Veriler";
                        totalCount = analysisResult.FilterResult.FilteredData.Count;
                        break;
                    case "excluded":
                        var excludedData = GetExcludedData(analysisResult.FilterResult);
                        data = excludedData.Take(limit).ToList();
                        title = "Filtreye Uymayan Veriler";
                        totalCount = excludedData.Count;
                        break;
                    case "empty":
                        var emptyData = GetEmptyData(analysisResult.FilterResult);
                        data = emptyData.Take(limit).ToList();
                        title = "Boş Veriler";
                        totalCount = emptyData.Count;
                        break;
                    default:
                        return Json(new { success = false, message = "Geçersiz veri tipi." });
                }

                return Json(new
                {
                    success = true,
                    data = data,
                    headers = analysisResult.FilterResult.Headers,
                    title = title,
                    totalCount = totalCount,
                    displayedCount = data.Count,
                    hasMore = totalCount > limit
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Veri getirme sırasında hata");
                return Json(new { success = false, message = $"Hata: {ex.Message}" });
            }
        }

        [HttpPost]
        public async Task<IActionResult> ExportToExcel(string dataType = "filtered")
        {
            try
            {
                var analysisResultJson = HttpContext.Session.GetString("AnalysisResult");
                if (string.IsNullOrEmpty(analysisResultJson))
                {
                    return Json(new { success = false, message = "Önce analiz yapmalısınız." });
                }

                var analysisResult = System.Text.Json.JsonSerializer.Deserialize<DataAnalysisResult>(analysisResultJson);

                byte[] excelData;
                string fileName;

                switch (dataType)
                {
                    case "filtered":
                        excelData = GenerateExcel(analysisResult.FilterResult.FilteredData, analysisResult.FilterResult.Headers, "Filtrelenmiş Veriler");
                        fileName = $"filtrelenmis_veriler_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
                        break;
                    case "excluded":
                        var excludedData = GetExcludedData(analysisResult.FilterResult);
                        excelData = GenerateExcel(excludedData, analysisResult.FilterResult.Headers, "Filtreye Uymayan Veriler");
                        fileName = $"uymeyen_veriler_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
                        break;
                    case "empty":
                        var emptyData = GetEmptyData(analysisResult.FilterResult);
                        excelData = GenerateExcel(emptyData, analysisResult.FilterResult.Headers, "Boş Veriler");
                        fileName = $"bos_veriler_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
                        break;
                    default:
                        return Json(new { success = false, message = "Geçersiz veri tipi." });
                }

                return File(excelData, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Excel export sırasında hata");
                return Json(new { success = false, message = $"Export hatası: {ex.Message}" });
            }
        }

        [HttpPost]
        public IActionResult AddFilter(AdvancedAnalysisModel model, string columnName, string value, FilterType filterType)
        {
            if (!string.IsNullOrEmpty(columnName) && !string.IsNullOrEmpty(value))
            {
                model.Filters.Add(new FilterCriteria
                {
                    ColumnName = columnName,
                    Value = value,
                    FilterType = filterType,
                    Description = GenerateFilterDescription(columnName, value, filterType)
                });
            }

            ViewBag.FilterPresets = GetFilterPresets();
            return View("Index", model);
        }

        [HttpPost]
        public IActionResult RemoveFilter(AdvancedAnalysisModel model, int filterIndex)
        {
            if (filterIndex >= 0 && filterIndex < model.Filters.Count)
            {
                model.Filters.RemoveAt(filterIndex);
            }

            ViewBag.FilterPresets = GetFilterPresets();
            return View("Index", model);
        }

        [HttpPost]
        public IActionResult ApplyPreset(AdvancedAnalysisModel model, string presetName)
        {
            var presets = GetFilterPresets();
            var preset = presets.FirstOrDefault(p => p.Name == presetName);

            if (preset != null)
            {
                model.Filters.Clear();
                model.Filters.AddRange(preset.Filters);
                ViewBag.Success = $"'{preset.Name}' filtre seti uygulandı.";
            }

            ViewBag.FilterPresets = GetFilterPresets();
            return View("Index", model);
        }

        private byte[] GenerateExcel(List<Dictionary<string, object>> data, List<string> headers, string sheetName)
        {
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

            using var package = new ExcelPackage();
            var worksheet = package.Workbook.Worksheets.Add(sheetName);

            // Headers
            for (int i = 0; i < headers.Count; i++)
            {
                worksheet.Cells[1, i + 1].Value = headers[i];
                worksheet.Cells[1, i + 1].Style.Font.Bold = true;
                worksheet.Cells[1, i + 1].Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                worksheet.Cells[1, i + 1].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightBlue);
            }

            // Data
            for (int row = 0; row < data.Count; row++)
            {
                var rowData = data[row];
                for (int col = 0; col < headers.Count; col++)
                {
                    var header = headers[col];
                    var value = rowData.ContainsKey(header) ? rowData[header]?.ToString() ?? "" : "";
                    worksheet.Cells[row + 2, col + 1].Value = value;
                }
            }

            // Auto-fit columns
            worksheet.Cells.AutoFitColumns();

            return package.GetAsByteArray();
        }

        private List<Dictionary<string, object>> GetExcludedData(FilteredDataResult result)
        {
            var filteredKeys = new HashSet<string>();

            foreach (var filteredRow in result.FilteredData)
            {
                var key = string.Join("|", result.Headers.Select(h =>
                    filteredRow.ContainsKey(h) ? filteredRow[h]?.ToString() ?? "" : ""));
                filteredKeys.Add(key);
            }

            var excludedData = new List<Dictionary<string, object>>();

            foreach (var allRow in result.AllData)
            {
                var key = string.Join("|", result.Headers.Select(h =>
                    allRow.ContainsKey(h) ? allRow[h]?.ToString() ?? "" : ""));

                if (!filteredKeys.Contains(key))
                {
                    excludedData.Add(allRow);
                }
            }

            return excludedData;
        }

        private List<Dictionary<string, object>> GetEmptyData(FilteredDataResult result)
        {
            var emptyData = new List<Dictionary<string, object>>();

            foreach (var row in result.AllData)
            {
                bool hasEmptyFields = false;

                foreach (var header in result.Headers)
                {
                    if (!row.ContainsKey(header) || string.IsNullOrWhiteSpace(row[header]?.ToString()))
                    {
                        hasEmptyFields = true;
                        break;
                    }
                }

                if (hasEmptyFields)
                {
                    emptyData.Add(row);
                }
            }

            return emptyData;
        }

        private List<FilterPreset> GetFilterPresets()
        {
            return new List<FilterPreset>
            {
                new FilterPreset
                {
                    Name = "Gmail Kullanıcıları",
                    Description = "Gmail hesabı olan kişiler",
                    Icon = "fas fa-envelope",
                    Color = "danger",
                    Filters = new List<FilterCriteria>
                    {
                        new FilterCriteria
                        {
                            ColumnName = "Email",
                            Value = "gmail.com",
                            FilterType = FilterType.EmailDomain,
                            Description = "Gmail domain kontrolü"
                        }
                    }
                },
                new FilterPreset
                {
                    Name = "Hotmail Kullanıcıları",
                    Description = "Hotmail/Outlook hesabı olan kişiler",
                    Icon = "fas fa-envelope",
                    Color = "info",
                    Filters = new List<FilterCriteria>
                    {
                        new FilterCriteria
                        {
                            ColumnName = "Email",
                            Value = "hotmail.com",
                            FilterType = FilterType.EmailDomain,
                            Description = "Hotmail domain kontrolü"
                        }
                    }
                },
                new FilterPreset
                {
                    Name = "Kurumsal Email (@samsun.bel.tr)",
                    Description = "Samsun Büyükşehir Belediyesi email adresleri",
                    Icon = "fas fa-building",
                    Color = "success",
                    Filters = new List<FilterCriteria>
                    {
                        new FilterCriteria
                        {
                            ColumnName = "Email",
                            Value = "samsun.bel.tr",
                            FilterType = FilterType.EmailDomain,
                            Description = "Samsun Belediyesi domain kontrolü"
                        }
                    }
                }
            };
        }

        private Dictionary<string, int> GenerateColumnStatistics(List<Dictionary<string, object>> data, List<string> headers)
        {
            var stats = new Dictionary<string, int>();

            foreach (var header in headers)
            {
                var nonEmptyCount = data.Count(row =>
                    row.ContainsKey(header) &&
                    !string.IsNullOrWhiteSpace(row[header]?.ToString()));

                stats[header] = nonEmptyCount;
            }

            return stats;
        }

        private Dictionary<string, Dictionary<string, int>> GenerateValueDistribution(List<Dictionary<string, object>> data, List<string> headers)
        {
            var distribution = new Dictionary<string, Dictionary<string, int>>();

            foreach (var header in headers.Take(5))
            {
                var valueGroups = data
                    .Where(row => row.ContainsKey(header))
                    .GroupBy(row => row[header]?.ToString()?.Trim() ?? "Boş")
                    .ToDictionary(g => g.Key, g => g.Count());

                distribution[header] = valueGroups
                    .OrderByDescending(kvp => kvp.Value)
                    .Take(10)
                    .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            }

            return distribution;
        }

        private List<string> GenerateInsights(FilteredDataResult result)
        {
            var insights = new List<string>();

            var filterRatio = result.TotalRecords > 0
                ? (double)result.FilteredRecords / result.TotalRecords * 100
                : 0;

            insights.Add($"📊 Filtreleme oranı: %{filterRatio:F1} ({result.FilteredRecords}/{result.TotalRecords})");

            if (filterRatio > 70)
                insights.Add("⚠️ Filtrelenen kayıt oranı çok yüksek.");
            else if (filterRatio < 10)
                insights.Add("ℹ️ Filtrelenen kayıt oranı düşük.");
            else
                insights.Add("✅ Filtreleme oranı dengeli.");

            return insights;
        }

        private string GenerateFilterDescription(string columnName, string value, FilterType filterType)
        {
            return filterType switch
            {
                FilterType.Contains => $"'{columnName}' sütununda '{value}' içeren",
                FilterType.Equals => $"'{columnName}' sütunu '{value}' olan",
                FilterType.StartsWith => $"'{columnName}' sütunu '{value}' ile başlayan",
                FilterType.EndsWith => $"'{columnName}' sütunu '{value}' ile biten",
                FilterType.EmailDomain => $"'{columnName}' sütununda '@{value}' domain olan",
                FilterType.TcPrefix => $"'{columnName}' sütunu '{value}' ile başlayan TC",
                FilterType.PhoneSuffix => $"'{columnName}' sütunu '{value}' ile biten telefon",
                FilterType.Regex => $"'{columnName}' sütununda regex '{value}' eşleşen",
                _ => $"'{columnName}' sütununda '{value}' filtresi"
            };
        }
    }
}
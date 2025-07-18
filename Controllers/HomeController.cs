using ExcelComparator.Models;
using ExcelComparator.Services;
using Microsoft.AspNetCore.Mvc;
using OfficeOpenXml;
using System.Diagnostics;
using System.Text.Json;

namespace ExcelComparator.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly IExcelService _excelService;

        public HomeController(ILogger<HomeController> logger, IExcelService excelService)
        {
            _logger = logger;
            _excelService = excelService;
        }

        public IActionResult Index()
        {
            return View(new ExcelComparisonModel());
        }

        [HttpPost]
        public async Task<IActionResult> Compare(ExcelComparisonModel model)
        {
            try
            {
                if (model.MainExcelFile == null)
                {
                    ViewBag.Error = "Ana Excel dosyası seçilmelidir.";
                    return View("Index", model);
                }

                if (!model.ComparisonFiles.Any())
                {
                    ViewBag.Error = "En az bir karşılaştırma dosyası seçilmelidir.";
                    return View("Index", model);
                }

                var results = await _excelService.CompareExcelFiles(
                    model.MainExcelFile,
                    model.ComparisonFiles,
                    model.PrimaryKeyColumns ?? "Ad Soyad");

                // Burada OnlyInMain ve OnlyInComparison satır verilerini dolduruyoruz
                foreach (var result in results)
                {
                    result.OnlyInMainRows = new List<Dictionary<string, object>>();
                    result.OnlyInComparisonRows = new List<Dictionary<string, object>>();

                    var keyColumns = ParseKeyColumns(model.PrimaryKeyColumns ?? "Ad Soyad");

                    var mainSheets = await _excelService.ReadExcelFile(model.MainExcelFile);
                    var mainSheet = mainSheets.FirstOrDefault();

                    var compSheets = await _excelService.ReadExcelFile(model.ComparisonFiles.FirstOrDefault());
                    var compSheet = compSheets?.FirstOrDefault();

                    // Ana dosyanın verileri key'e göre sözlükte tutuluyor
                    var mainKeyMapping = CreateKeyMapping(mainSheet.Data, keyColumns);
                    // Karşılaştırma dosyasının verileri
                    var compKeyMapping = compSheet != null ? CreateKeyMapping(compSheet.Data, keyColumns) : new Dictionary<string, Dictionary<string, object>>();

                    // Boşsa direkt atla
                    if (mainKeyMapping == null || compKeyMapping == null) continue;

                    // OnlyInMain satırları ekle
                    foreach (var key in result.OnlyInMain)
                    {
                        if (mainKeyMapping.TryGetValue(key, out var row))
                            result.OnlyInMainRows.Add(row);
                    }

                    // OnlyInComparison satırları ekle
                    foreach (var key in result.OnlyInComparison)
                    {
                        if (compKeyMapping.TryGetValue(key, out var row))
                            result.OnlyInComparisonRows.Add(row);
                    }
                }

                // Sonuçları disk üzerine JSON olarak kaydet
                var fileName = $"comparison_{Guid.NewGuid()}.json";
                var tempFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "temp");
                if (!Directory.Exists(tempFolder))
                {
                    Directory.CreateDirectory(tempFolder);
                }
                var filePath = Path.Combine(tempFolder, fileName);
                System.IO.File.WriteAllText(filePath, JsonSerializer.Serialize(results));

                TempData["ComparisonFile"] = fileName;

                ViewBag.Results = results;
                ViewBag.Success = "Karşılaştırma başarıyla tamamlandı!";

                return View("Index", model);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Karşılaştırma sırasında hata");
                ViewBag.Error = $"Hata: {ex.Message}";
                return View("Index", model);
            }
        }

        [HttpGet]
        public IActionResult DownloadResult(string type)
        {
            if (!TempData.ContainsKey("ComparisonFile"))
            {
                TempData["Error"] = "İndirilecek sonuç bulunamadı.";
                return RedirectToAction("Index");
            }

            var fileName = TempData["ComparisonFile"] as string;
            var tempFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "temp");
            var filePath = Path.Combine(tempFolder, fileName);

            if (!System.IO.File.Exists(filePath))
            {
                TempData["Error"] = "Sonuç dosyası bulunamadı.";
                return RedirectToAction("Index");
            }

            var json = System.IO.File.ReadAllText(filePath);
            var results = JsonSerializer.Deserialize<List<ComparisonResult>>(json);

            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

            using var package = new ExcelPackage();
            var worksheet = package.Workbook.Worksheets.Add("Sonuçlar");

            var headers = new List<string> { "Anahtar", "Sütun Adı", "Ana Dosya Değeri", "Karşılaştırma Değeri" };
            for (int i = 0; i < headers.Count; i++)
            {
                worksheet.Cells[1, i + 1].Value = headers[i];
                worksheet.Cells[1, i + 1].Style.Font.Bold = true;
            }

            int rowIndex = 2;

            foreach (var result in results)
            {
                var keyColumns = ParseKeyColumns("Ad Soyad"); // Veya kullanıcının seçtiği

                List<RowComparison> selectedComparisons = type switch
                {
                    "exact" => result.Matches,
                    "partial" => result.Mismatches,
                    _ => new List<RowComparison>()
                };

                if (type == "onlyMain")
                {
                    foreach (var row in result.OnlyInMainRows)
                    {
                        foreach (var col in row.Keys)
                        {
                            worksheet.Cells[rowIndex, 1].Value = string.Join(" | ", keyColumns.Select(k => row.ContainsKey(k) ? row[k]?.ToString() : ""));
                            worksheet.Cells[rowIndex, 2].Value = col;
                            worksheet.Cells[rowIndex, 3].Value = row[col]?.ToString();
                            worksheet.Cells[rowIndex, 4].Value = "";
                            rowIndex++;
                        }
                    }
                    continue;
                }
                else if (type == "onlyComparison")
                {
                    foreach (var row in result.OnlyInComparisonRows)
                    {
                        foreach (var col in row.Keys)
                        {
                            worksheet.Cells[rowIndex, 1].Value = string.Join(" | ", keyColumns.Select(k => row.ContainsKey(k) ? row[k]?.ToString() : ""));
                            worksheet.Cells[rowIndex, 2].Value = col;
                            worksheet.Cells[rowIndex, 3].Value = "";
                            worksheet.Cells[rowIndex, 4].Value = row[col]?.ToString();
                            rowIndex++;
                        }
                    }
                    continue;
                }

                // exact ve partial için normal satır
                foreach (var comp in selectedComparisons)
                {
                    if (comp.MainData != null && comp.ComparisonData != null)
                    {
                        var allColumns = comp.MainData.Keys.Union(comp.ComparisonData.Keys).Distinct();
                        foreach (var col in allColumns)
                        {
                            worksheet.Cells[rowIndex, 1].Value = comp.PrimaryKey;
                            worksheet.Cells[rowIndex, 2].Value = col;
                            worksheet.Cells[rowIndex, 3].Value = comp.MainData.ContainsKey(col) ? comp.MainData[col] : "";
                            worksheet.Cells[rowIndex, 4].Value = comp.ComparisonData.ContainsKey(col) ? comp.ComparisonData[col] : "";
                            rowIndex++;
                        }
                    }
                    else
                    {
                        worksheet.Cells[rowIndex, 1].Value = comp.PrimaryKey;
                        worksheet.Cells[rowIndex, 2].Value = "";
                        worksheet.Cells[rowIndex, 3].Value = type == "onlyMain" ? "Var" : "";
                        worksheet.Cells[rowIndex, 4].Value = type == "onlyComparison" ? "Var" : "";
                        rowIndex++;
                    }
                }
            }

            System.IO.File.Delete(filePath);

            var fileBytes = package.GetAsByteArray();
            var excelFileName = $"Comparison_{type}_{DateTime.Now:yyyyMMddHHmmss}.xlsx";

            return File(fileBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", excelFileName);
        }

        [HttpPost]
        public IActionResult Clear()
        {
            TempData["Success"] = "Sayfa başarıyla sıfırlandı!";
            return RedirectToAction("Index");
        }

        private List<string> ParseKeyColumns(string primaryKeyColumns) =>
            primaryKeyColumns?.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(k => k.Trim()).ToList() ?? new List<string> { "Ad Soyad" };

        private Dictionary<string, Dictionary<string, object>> CreateKeyMapping(List<Dictionary<string, object>> data, List<string> keyColumns)
        {
            var keyMapping = new Dictionary<string, Dictionary<string, object>>();

            foreach (var row in data)
            {
                var keyValues = new List<string>();
                bool hasAllKeys = true;

                foreach (var keyColumn in keyColumns)
                {
                    if (row.ContainsKey(keyColumn) && row[keyColumn] != null)
                    {
                        var value = row[keyColumn].ToString()?.Trim().ToLower() ?? "";
                        if (!string.IsNullOrEmpty(value))
                        {
                            keyValues.Add(value);
                        }
                        else
                        {
                            hasAllKeys = false;
                            break;
                        }
                    }
                    else
                    {
                        hasAllKeys = false;
                        break;
                    }
                }

                if (hasAllKeys && keyValues.Any())
                {
                    var combinedKey = string.Join(" | ", keyValues);
                    if (!keyMapping.ContainsKey(combinedKey))
                    {
                        keyMapping[combinedKey] = row;
                    }
                }
            }

            return keyMapping;
        }
    }
}

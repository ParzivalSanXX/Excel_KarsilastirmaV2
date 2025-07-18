// Services/ExcelService.cs - TC Kimlik ve Filtreleme Desteği
using ExcelComparator.Models;
using ExcelComparator.Services;
using OfficeOpenXml;
using System.Text.RegularExpressions;

namespace ExcelComparator.Services
{
    public class ExcelService : IExcelService
    {
        private readonly ILogger<ExcelService> _logger;

        public ExcelService(ILogger<ExcelService> logger)
        {
            _logger = logger;
        }

        // TC Kimlik esnek karşılaştırma
        private bool CompareFlexibleValues(string value1, string value2, string columnName)
        {
            if (string.IsNullOrEmpty(value1) || string.IsNullOrEmpty(value2))
                return false;

            // Standart karşılaştırma
            if (string.Equals(value1, value2, StringComparison.OrdinalIgnoreCase))
                return true;

            // TC Kimlik özel karşılaştırması
            if (IsTcColumn(columnName))
            {
                return CompareTcNumbers(value1, value2);
            }

            // Email domain karşılaştırması
            if (IsEmailColumn(columnName))
            {
                return CompareEmails(value1, value2);
            }

            // Telefon numarası karşılaştırması
            if (IsPhoneColumn(columnName))
            {
                return ComparePhones(value1, value2);
            }

            return false;
        }

        private bool IsTcColumn(string columnName)
        {
            var tcKeywords = new[] { "tc", "kimlik", "tckn", "tcno", "tc_no", "tc kimlik", "tckimlik" };
            return tcKeywords.Any(keyword =>
                columnName.ToLower().Contains(keyword));
        }

        private bool IsEmailColumn(string columnName)
        {
            var emailKeywords = new[] { "email", "mail", "eposta", "e-mail", "e_mail" };
            return emailKeywords.Any(keyword =>
                columnName.ToLower().Contains(keyword));
        }

        private bool IsPhoneColumn(string columnName)
        {
            var phoneKeywords = new[] { "telefon", "phone", "tel", "gsm", "cep", "mobile" };
            return phoneKeywords.Any(keyword =>
                columnName.ToLower().Contains(keyword));
        }

        private bool CompareTcNumbers(string tc1, string tc2)
        {
            // Sadece rakamları al
            var cleanTc1 = Regex.Replace(tc1, @"[^\d]", "");
            var cleanTc2 = Regex.Replace(tc2, @"[^\d]", "");

            // Boş ise false
            if (string.IsNullOrEmpty(cleanTc1) || string.IsNullOrEmpty(cleanTc2))
                return false;

            // Tam eşleşme
            if (cleanTc1 == cleanTc2)
                return true;

            // Kısmi eşleşme kontrolü
            // İlk 3 rakam + son 2 rakam karşılaştırması
            if (cleanTc1.Length >= 5 && cleanTc2.Length >= 5)
            {
                var prefix1 = cleanTc1.Substring(0, 3);
                var suffix1 = cleanTc1.Substring(cleanTc1.Length - 2);

                var prefix2 = cleanTc2.Substring(0, 3);
                var suffix2 = cleanTc2.Substring(cleanTc2.Length - 2);

                if (prefix1 == prefix2 && suffix1 == suffix2)
                {
                    _logger.LogInformation($"TC kısmi eşleşme: {tc1} ≈ {tc2}");
                    return true;
                }
            }

            // İlk 3 rakam karşılaştırması (en kısa durumda)
            if (cleanTc1.Length >= 3 && cleanTc2.Length >= 3)
            {
                var prefix1 = cleanTc1.Substring(0, 3);
                var prefix2 = cleanTc2.Substring(0, 3);

                if (prefix1 == prefix2)
                {
                    _logger.LogInformation($"TC prefix eşleşme: {tc1} ≈ {tc2}");
                    return true;
                }
            }

            return false;
        }

        private bool CompareEmails(string email1, string email2)
        {
            // Tam eşleşme
            if (string.Equals(email1, email2, StringComparison.OrdinalIgnoreCase))
                return true;

            // Domain karşılaştırması
            try
            {
                var domain1 = email1.Split('@').LastOrDefault()?.ToLower();
                var domain2 = email2.Split('@').LastOrDefault()?.ToLower();

                if (!string.IsNullOrEmpty(domain1) && !string.IsNullOrEmpty(domain2))
                {
                    return domain1 == domain2;
                }
            }
            catch { }

            return false;
        }

        private bool ComparePhones(string phone1, string phone2)
        {
            // Sadece rakamları al
            var cleanPhone1 = Regex.Replace(phone1, @"[^\d]", "");
            var cleanPhone2 = Regex.Replace(phone2, @"[^\d]", "");

            if (string.IsNullOrEmpty(cleanPhone1) || string.IsNullOrEmpty(cleanPhone2))
                return false;

            // Tam eşleşme
            if (cleanPhone1 == cleanPhone2)
                return true;

            // Son 7 rakam karşılaştırması (Türkiye için)
            if (cleanPhone1.Length >= 7 && cleanPhone2.Length >= 7)
            {
                var suffix1 = cleanPhone1.Substring(cleanPhone1.Length - 7);
                var suffix2 = cleanPhone2.Substring(cleanPhone2.Length - 7);
                return suffix1 == suffix2;
            }

            return false;
        }

        // Filtreleme fonksiyonları
        public async Task<FilteredDataResult> FilterData(IFormFile file, List<FilterCriteria> filters)
        {
            var result = new FilteredDataResult();

            try
            {
                var sheets = await ReadExcelFile(file);
                var sheet = sheets.FirstOrDefault();

                if (sheet == null || !sheet.Data.Any())
                {
                    result.ErrorMessage = "Dosya boş veya okunamadı";
                    return result;
                }

                result.TotalRecords = sheet.Data.Count;
                result.Headers = sheet.Headers;
                result.AllData = sheet.Data;

                // Filtreleri uygula
                var filteredData = sheet.Data.ToList();

                foreach (var filter in filters)
                {
                    filteredData = ApplyFilter(filteredData, filter);
                }

                result.FilteredData = filteredData;
                result.FilteredRecords = filteredData.Count;
                result.Success = true;

                _logger.LogInformation($"Filtreleme tamamlandı: {result.FilteredRecords}/{result.TotalRecords} kayıt");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Filtreleme sırasında hata");
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        private List<Dictionary<string, object>> ApplyFilter(
            List<Dictionary<string, object>> data,
            FilterCriteria filter)
        {
            if (string.IsNullOrEmpty(filter.ColumnName) || string.IsNullOrEmpty(filter.Value))
                return data;

            return data.Where(row =>
            {
                if (!row.ContainsKey(filter.ColumnName))
                    return false;

                var cellValue = row[filter.ColumnName]?.ToString() ?? "";

                return filter.FilterType switch
                {
                    FilterType.Contains => cellValue.Contains(filter.Value, StringComparison.OrdinalIgnoreCase),
                    FilterType.Equals => string.Equals(cellValue, filter.Value, StringComparison.OrdinalIgnoreCase),
                    FilterType.StartsWith => cellValue.StartsWith(filter.Value, StringComparison.OrdinalIgnoreCase),
                    FilterType.EndsWith => cellValue.EndsWith(filter.Value, StringComparison.OrdinalIgnoreCase),
                    FilterType.EmailDomain => CheckEmailDomain(cellValue, filter.Value),
                    FilterType.TcPrefix => CheckTcPrefix(cellValue, filter.Value),
                    FilterType.PhoneSuffix => CheckPhoneSuffix(cellValue, filter.Value),
                    FilterType.Regex => CheckRegex(cellValue, filter.Value),
                    _ => false
                };
            }).ToList();
        }

        private bool CheckEmailDomain(string email, string domain)
        {
            try
            {
                if (string.IsNullOrEmpty(email) || !email.Contains('@'))
                    return false;

                var emailDomain = email.Split('@').LastOrDefault()?.ToLower();
                var targetDomain = domain.ToLower().Replace("@", "");

                return emailDomain == targetDomain;
            }
            catch
            {
                return false;
            }
        }

        private bool CheckTcPrefix(string tc, string prefix)
        {
            var cleanTc = Regex.Replace(tc, @"[^\d]", "");
            return cleanTc.StartsWith(prefix);
        }

        private bool CheckPhoneSuffix(string phone, string suffix)
        {
            var cleanPhone = Regex.Replace(phone, @"[^\d]", "");
            return cleanPhone.EndsWith(suffix);
        }

        private bool CheckRegex(string value, string pattern)
        {
            try
            {
                return Regex.IsMatch(value, pattern, RegexOptions.IgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        // Mevcut metotlar... (ReadExcelFile, CompareExcelFiles vb.)
        public async Task<List<ExcelSheet>> ReadExcelFile(IFormFile file)
        {
            var sheets = new List<ExcelSheet>();

            try
            {
                ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

                using var stream = new MemoryStream();
                await file.CopyToAsync(stream);
                stream.Position = 0;

                using var package = new ExcelPackage(stream);

                foreach (var worksheet in package.Workbook.Worksheets)
                {
                    var sheet = new ExcelSheet { SheetName = worksheet.Name };

                    if (worksheet.Dimension == null) continue;

                    // Başlıkları al
                    for (int col = 1; col <= worksheet.Dimension.End.Column; col++)
                    {
                        var headerValue = worksheet.Cells[1, col].Value?.ToString()?.Trim() ?? $"Column{col}";
                        sheet.Headers.Add(headerValue);
                    }

                    // Veri satırlarını al
                    for (int row = 2; row <= worksheet.Dimension.End.Row; row++)
                    {
                        var rowData = new Dictionary<string, object>();
                        bool hasData = false;

                        for (int col = 1; col <= worksheet.Dimension.End.Column; col++)
                        {
                            var header = sheet.Headers[col - 1];
                            var value = worksheet.Cells[row, col].Value?.ToString()?.Trim() ?? "";
                            rowData[header] = value;

                            if (!string.IsNullOrEmpty(value)) hasData = true;
                        }

                        if (hasData) sheet.Data.Add(rowData);
                    }

                    sheets.Add(sheet);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Excel dosyası okunurken hata");
                throw;
            }

            return sheets;
        }

        public async Task<List<ComparisonResult>> CompareExcelFiles(
            IFormFile mainFile,
            List<IFormFile> comparisonFiles,
            string primaryKeyColumns)
        {
            var results = new List<ComparisonResult>();

            try
            {
                var keyColumns = ParseKeyColumns(primaryKeyColumns);
                var mainSheets = await ReadExcelFile(mainFile);
                var mainSheet = mainSheets.FirstOrDefault();

                if (mainSheet == null || !mainSheet.Data.Any())
                {
                    throw new Exception("Ana Excel dosyası boş veya geçersiz");
                }

                var availableMainKeys = ValidateAndMapKeyColumns(mainSheet.Headers, keyColumns);

                foreach (var compFile in comparisonFiles)
                {
                    try
                    {
                        var comparisonSheets = await ReadExcelFile(compFile);
                        var comparisonSheet = comparisonSheets.FirstOrDefault();

                        if (comparisonSheet == null || !comparisonSheet.Data.Any())
                        {
                            results.Add(new ComparisonResult
                            {
                                FileName = compFile.FileName ?? "Bilinmeyen Dosya",
                                Summary = "Dosya boş veya okunamadı"
                            });
                            continue;
                        }

                        var availableCompKeys = ValidateAndMapKeyColumns(comparisonSheet.Headers, keyColumns);
                        var commonKeys = availableMainKeys.Intersect(availableCompKeys).ToList();

                        if (!commonKeys.Any())
                        {
                            results.Add(new ComparisonResult
                            {
                                FileName = compFile.FileName ?? "Bilinmeyen Dosya",
                                Summary = "Ortak anahtar sütun bulunamadı"
                            });
                            continue;
                        }

                        var result = CompareSheets(mainSheet, comparisonSheet, commonKeys);
                        result.FileName = compFile.FileName ?? "Bilinmeyen Dosya";
                        result.UsedKeyColumns = commonKeys;
                        results.Add(result);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Dosya '{compFile.FileName}' işlenirken hata");
                        results.Add(new ComparisonResult
                        {
                            FileName = compFile.FileName ?? "Bilinmeyen Dosya",
                            Summary = $"Hata: {ex.Message}"
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Karşılaştırma sırasında hata");
                throw;
            }

            return results;
        }

        private ComparisonResult CompareSheets(ExcelSheet mainSheet, ExcelSheet comparisonSheet, List<string> keyColumns)
        {
            var result = new ComparisonResult { UsedKeyColumns = keyColumns };

            var mainKeys = CreateKeyMapping(mainSheet.Data, keyColumns);
            var comparisonKeys = CreateKeyMapping(comparisonSheet.Data, keyColumns);

            foreach (var mainKey in mainKeys.Keys)
            {
                if (comparisonKeys.ContainsKey(mainKey))
                {
                    var comparison = CompareRows(mainKeys[mainKey], comparisonKeys[mainKey], mainKey, keyColumns);

                    if (comparison.MatchPercentage >= 80)
                        result.Matches.Add(comparison);
                    else
                        result.Mismatches.Add(comparison);
                }
                else
                {
                    result.OnlyInMain.Add(mainKey);
                }
            }

            foreach (var compKey in comparisonKeys.Keys)
            {
                if (!mainKeys.ContainsKey(compKey))
                {
                    result.OnlyInComparison.Add(compKey);
                }
            }

            result.Summary = $"Tam: {result.Matches.Count}, Kısmi: {result.Mismatches.Count}, Ana: {result.OnlyInMain.Count}, Karş: {result.OnlyInComparison.Count}";
            return result;
        }

        private RowComparison CompareRows(Dictionary<string, object> mainRow, Dictionary<string, object> comparisonRow, string combinedKey, List<string> keyColumns)
        {
            var comparison = new RowComparison
            {
                PrimaryKey = combinedKey,
                MainData = mainRow,
                ComparisonData = comparisonRow
            };

            foreach (var keyColumn in keyColumns)
            {
                if (mainRow.ContainsKey(keyColumn))
                {
                    comparison.KeyValues[keyColumn] = mainRow[keyColumn]?.ToString() ?? "";
                }
            }

            var commonColumns = mainRow.Keys.Intersect(comparisonRow.Keys).ToList();

            foreach (var column in commonColumns)
            {
                var mainValue = mainRow[column]?.ToString()?.Trim() ?? "";
                var compValue = comparisonRow[column]?.ToString()?.Trim() ?? "";

                // Esnek karşılaştırma kullan
                if (CompareFlexibleValues(mainValue, compValue, column))
                {
                    comparison.MatchingColumns.Add(column);
                }
                else
                {
                    comparison.MismatchingColumns.Add(column);
                }
            }

            if (commonColumns.Count > 0)
            {
                comparison.MatchPercentage = (double)comparison.MatchingColumns.Count / commonColumns.Count * 100;
            }

            return comparison;
        }

        // Yardımcı metotlar...
        private List<string> ParseKeyColumns(string primaryKeyColumns) =>
            primaryKeyColumns?.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(k => k.Trim()).ToList() ?? new List<string> { "Ad Soyad" };

        private List<string> ValidateAndMapKeyColumns(List<string> headers, List<string> requestedKeys) =>
            requestedKeys.Select(key => headers.FirstOrDefault(h =>
                string.Equals(h, key, StringComparison.OrdinalIgnoreCase)) ??
                FindSimilarColumn(headers, key)).Where(k => k != null).ToList();

        private string? FindSimilarColumn(List<string> headers, string targetColumn) =>
            headers.FirstOrDefault(h => h.ToLower().Contains(targetColumn.ToLower()) ||
                targetColumn.ToLower().Contains(h.ToLower()));

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
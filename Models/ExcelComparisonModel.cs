// Models/ExcelComparisonModel.cs - Güncellenmiş
namespace ExcelComparator.Models
{
    public class ExcelComparisonModel
    {
        public IFormFile? MainExcelFile { get; set; }
        public List<IFormFile> ComparisonFiles { get; set; } = new List<IFormFile>();

        // Çoklu anahtar sütun desteği
        public string PrimaryKeyColumns { get; set; } = "Ad Soyad"; // Virgülle ayrılmış
        public bool UseMultipleKeys { get; set; } = false;

        // Anahtar sütunları liste olarak döndürür
        public List<string> GetKeyColumns()
        {
            if (string.IsNullOrWhiteSpace(PrimaryKeyColumns))
                return new List<string> { "Ad Soyad" };

            return PrimaryKeyColumns
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(k => k.Trim())
                .ToList();
        }
    }

    public class ComparisonResult
    {
        public string FileName { get; set; } = string.Empty;
        public List<RowComparison> Matches { get; set; } = new List<RowComparison>();
        public List<RowComparison> Mismatches { get; set; } = new List<RowComparison>();
        public List<string> OnlyInMain { get; set; } = new List<string>();
        public List<string> OnlyInComparison { get; set; } = new List<string>();
        public string Summary { get; set; } = string.Empty;

        public List<Dictionary<string, object>> OnlyInMainRows { get; set; } = new List<Dictionary<string, object>>();
        public List<Dictionary<string, object>> OnlyInComparisonRows { get; set; } = new List<Dictionary<string, object>>();


        // Çoklu anahtar bilgisi
        public List<string> UsedKeyColumns { get; set; } = new List<string>();
        public string KeyCombinationMethod { get; set; } = "single"; // "single", "combined", "any"
    }

    public class RowComparison
    {
        public string PrimaryKey { get; set; } = string.Empty;
        public Dictionary<string, object> MainData { get; set; } = new Dictionary<string, object>();
        public Dictionary<string, object> ComparisonData { get; set; } = new Dictionary<string, object>();
        public List<string> MatchingColumns { get; set; } = new List<string>();
        public List<string> MismatchingColumns { get; set; } = new List<string>();
        public double MatchPercentage { get; set; }

        // Çoklu anahtar için
        public Dictionary<string, string> KeyValues { get; set; } = new Dictionary<string, string>();
        public bool IsPartialKeyMatch { get; set; } = false;
    }

    public class ExcelSheet
    {
        public string SheetName { get; set; } = string.Empty;
        public List<string> Headers { get; set; } = new List<string>();
        public List<Dictionary<string, object>> Data { get; set; } = new List<Dictionary<string, object>>();
    }

    public class KeyMatchResult
    {
        public string CombinedKey { get; set; } = string.Empty;
        public Dictionary<string, string> IndividualKeys { get; set; } = new Dictionary<string, string>();
        public bool IsCompleteMatch { get; set; } = false;
        public int MatchedKeyCount { get; set; } = 0;
        public int TotalKeyCount { get; set; } = 0;
        public double KeyMatchPercentage => TotalKeyCount > 0 ? (double)MatchedKeyCount / TotalKeyCount * 100 : 0;
    }
}
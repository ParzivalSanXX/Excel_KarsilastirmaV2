namespace ExcelComparator.Models
{
    public class FilterCriteria
    {
        public string ColumnName { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public FilterType FilterType { get; set; } = FilterType.Contains;
        public string Description { get; set; } = string.Empty;
    }

    public enum FilterType
    {
        Contains,       // İçerir
        Equals,         // Eşittir
        StartsWith,     // İle başlar
        EndsWith,       // İle biter
        EmailDomain,    // Email domain
        TcPrefix,       // TC kimlik prefix
        PhoneSuffix,    // Telefon suffix
        Regex           // Regex pattern
    }

    public class FilteredDataResult
    {
        public bool Success { get; set; } = false;
        public string ErrorMessage { get; set; } = string.Empty;
        public List<string> Headers { get; set; } = new List<string>();
        public List<Dictionary<string, object>> AllData { get; set; } = new List<Dictionary<string, object>>();
        public List<Dictionary<string, object>> FilteredData { get; set; } = new List<Dictionary<string, object>>();
        public int TotalRecords { get; set; } = 0;
        public int FilteredRecords { get; set; } = 0;
        public List<FilterCriteria> AppliedFilters { get; set; } = new List<FilterCriteria>();
    }

    public class AdvancedAnalysisModel
    {
        public IFormFile? DataFile { get; set; }
        public List<FilterCriteria> Filters { get; set; } = new List<FilterCriteria>();
        public bool ShowOnlyFiltered { get; set; } = false;
        public bool ExportResults { get; set; } = false;
        public string AnalysisType { get; set; } = "filter"; // "filter", "compare", "analyze"
    }

    public class FilterPreset
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public List<FilterCriteria> Filters { get; set; } = new List<FilterCriteria>();
        public string Icon { get; set; } = "fas fa-filter";
        public string Color { get; set; } = "primary";
    }

    public class DataAnalysisResult
    {
        public FilteredDataResult FilterResult { get; set; } = new FilteredDataResult();
        public Dictionary<string, int> ColumnStatistics { get; set; } = new Dictionary<string, int>();
        public Dictionary<string, Dictionary<string, int>> ValueDistribution { get; set; } = new Dictionary<string, Dictionary<string, int>>();
        public List<string> Insights { get; set; } = new List<string>();
    }
}
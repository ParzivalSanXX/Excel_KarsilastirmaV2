// Services/IExcelService.cs - Güncellenmiş
using ExcelComparator.Models;

namespace ExcelComparator.Services
{
    public interface IExcelService
    {
        Task<List<ExcelSheet>> ReadExcelFile(IFormFile file);

        // Çoklu anahtar desteği için güncellenmiş metod
        Task<List<ComparisonResult>> CompareExcelFiles(
            IFormFile mainFile,
            List<IFormFile> comparisonFiles,
            string primaryKeyColumns); // Virgülle ayrılmış anahtar sütunlar

        // Filtreleme ve analiz metodları
        Task<FilteredDataResult> FilterData(IFormFile file, List<FilterCriteria> filters);
    }
}
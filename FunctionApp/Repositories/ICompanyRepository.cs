using System.Threading.Tasks;

namespace AGRechnung.FunctionApp.Repositories
{
    public interface ICompanyRepository
    {
        Task<int> CreateCompanyAsync(
            int userId,
            string name,
            string? vatNumber,
            string street,
            string postalCode,
            string city,
            string? country);
    }
}

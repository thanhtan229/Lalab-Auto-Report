using LalabAutoReport.Core.Interfaces;

namespace LalabAutoReport.Infrastructure.Data;

/// <summary>
/// SQLite implementation of print specification repository, extending SqliteProductRepository for V2 compatibility.
/// </summary>
public class SqlitePrintSpecificationRepository : SqliteProductRepository
{
    public SqlitePrintSpecificationRepository(ISqliteConnectionFactory connectionFactory)
        : base(connectionFactory)
    {
    }
}

using System.Data;
using SolutionCompakt.Server.Data;

namespace SolutionCompakt.Server.Security;

public sealed record CentralUserVerification(bool Allowed, string Message);

public sealed class CentralOperationalUserVerifier(CentralDbContext db)
{
    public async Task<CentralUserVerification> VerifyAsync(
        Guid companyId,
        int sourceUserId,
        string username,
        string role,
        CancellationToken cancellationToken)
    {
        if (sourceUserId < 1)
            return new(false, "Ungültiger Benutzer.");

        var schema = "company_" + companyId.ToString("N");
        var connection = db.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere)
            await connection.OpenAsync(cancellationToken);

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT \"Username\", \"Role\", \"IsActive\" FROM \"{schema}\".\"UserAccounts\" WHERE \"Id\" = @id LIMIT 1;";
            var id = command.CreateParameter();
            id.ParameterName = "id";
            id.Value = sourceUserId;
            command.Parameters.Add(id);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                return new(false, "Benutzer ist in der zentralen Datenbank nicht vorhanden.");

            var storedUsername = reader.GetString(0);
            var storedRole = reader.GetString(1);
            var isActive = reader.GetBoolean(2);
            if (!isActive)
                return new(false, "Benutzer ist deaktiviert.");
            if (!string.Equals(storedUsername, username?.Trim(), StringComparison.OrdinalIgnoreCase))
                return new(false, "Benutzername stimmt nicht mit der zentralen Datenbank überein.");
            if (!string.Equals(storedRole, role?.Trim(), StringComparison.Ordinal))
                return new(false, "Benutzerrolle wurde geändert. Bitte erneut anmelden.");

            return new(true, "Benutzer bestätigt.");
        }
        catch (Exception ex)
        {
            return new(false, "Zentraler Benutzer konnte nicht geprüft werden: " + ex.Message);
        }
        finally
        {
            if (openedHere)
                await connection.CloseAsync();
        }
    }
}

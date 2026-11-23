using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Finance_API.Persistence;
using Finance_API.Persistence.Entities;

namespace Finance_API.Endpoints;

/// <summary>
/// Records a transfer the user has made between two of their own accounts, so the payday plan sees the
/// money where it now is. Nothing is moved at the bank: this only records what the user did there.
/// </summary>
public static class TransfersEndpoints
{
    public static IEndpointRouteBuilder MapTransfersEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/transfers", RecordTransferAsync)
            .RequireAuthorization()
            .WithName("RecordTransfer")
            .WithTags("Transfers")
            .WithSummary("Record a transfer made between two owned accounts");

        return endpoints;
    }

    /// <summary>A transfer the user has made; Date defaults to today.</summary>
    public sealed record RecordTransferRequest(
        Guid FromAccountId,
        Guid ToAccountId,
        [Range(typeof(decimal), "0.01", "999999999")] decimal Amount,
        DateTime? Date = null);

    public sealed record TransferResponse(Guid FromBalanceId, Guid ToBalanceId, DateTime Date, decimal Amount);

    private static async Task<IResult> RecordTransferAsync(
        RecordTransferRequest request,
        FinanceDbContext db,
        ClaimsPrincipal user,
        Finance_API.Services.IUserContextService userContext)
    {
        if (request.FromAccountId == request.ToAccountId)
            return Results.BadRequest(new { errors = new[] { "Choose two different accounts." } });

        var date = (request.Date ?? DateTime.UtcNow).Date;
        if (date > DateTime.UtcNow.Date)
            return Results.BadRequest(new { errors = new[] { "The transfer date can't be in the future." } });

        var ownerId = userContext.GetUserId(user);
        var from = await db.Accounts.FindAsync(request.FromAccountId);
        var to = await db.Accounts.FindAsync(request.ToAccountId);
        if (from is null || to is null || from.OwnerId != ownerId || to.OwnerId != ownerId)
            return Results.NotFound();

        var outEntry = new Balance { AccountId = from.Id, Date = date, Amount = -request.Amount, Note = $"Transfer to {to.Name}" };
        var inEntry = new Balance { AccountId = to.Id, Date = date, Amount = request.Amount, Note = $"Transfer from {from.Name}" };
        db.Balances.AddRange(outEntry, inEntry);
        await db.SaveChangesAsync();

        return Results.Created(
            $"/api/accounts/{to.Id}/balances/{inEntry.Id}",
            new TransferResponse(outEntry.Id, inEntry.Id, date, request.Amount));
    }
}

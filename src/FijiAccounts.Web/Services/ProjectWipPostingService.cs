using System.Data;
using System.Text.Json;
using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Services;

public sealed class ProjectWipPostingService(
    ApplicationDbContext db,
    ProjectRevenueRecognitionService recognition,
    JournalPostingService journals,
    TenantAccessService access)
{
    public async Task<ProjectWipPosting> PostAsync(
        string userId,
        Guid organisationId,
        Guid projectId,
        DateOnly asAt,
        CancellationToken cancellationToken = default)
    {
        if (!await access.CanPostJournalsAsync(userId, organisationId))
        {
            throw new UnauthorizedAccessException(
                "You cannot post project WIP journals for this organisation.");
        }
        await using var transaction = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken)
            : null;

        var calculation = (await recognition.GetAsync(
                userId, organisationId, asAt, cancellationToken))
            .SingleOrDefault(x => x.ProjectId == projectId)
            ?? throw new UnauthorizedAccessException(
                "You cannot post WIP for this project.");
        if (await db.ProjectWipPostings.AnyAsync(x =>
                x.ProjectId == projectId && x.AsAt > asAt,
                cancellationToken))
        {
            throw new InvalidOperationException(
                "WIP cannot be posted for a date before a later WIP posting.");
        }
        var requiredWip = calculation.RevenueAdjustment
            ?? throw new InvalidOperationException(
                "Revenue recognition cannot be calculated for this project.");

        var project = await db.Projects.AsNoTracking().SingleAsync(x =>
            x.Id == projectId && x.OrganisationId == organisationId,
            cancellationToken);
        var organisation = await db.Organisations.AsNoTracking().SingleAsync(
            x => x.Id == organisationId,
            cancellationToken);
        if (organisation.ProjectContractAssetAccountId is not Guid contractAssetAccountId ||
            organisation.ProjectContractLiabilityAccountId is not Guid contractLiabilityAccountId ||
            organisation.ProjectRevenueRecognitionAccountId is not Guid revenueAccountId)
        {
            throw new InvalidOperationException(
                "Configure the project WIP accounts in organisation settings before posting.");
        }

        var previousWip = calculation.PostedWipAmount;
        var movement = Currency(requiredWip - previousWip);
        if (movement == 0m)
        {
            throw new InvalidOperationException(
                "No WIP journal movement is required for this project and date.");
        }

        var previousAsset = Math.Max(0m, previousWip);
        var requiredAsset = Math.Max(0m, requiredWip);
        var previousLiability = Math.Max(0m, -previousWip);
        var requiredLiability = Math.Max(0m, -requiredWip);
        var assetChange = Currency(requiredAsset - previousAsset);
        var liabilityChange = Currency(requiredLiability - previousLiability);
        var description = $"WIP true-up for {project.ProjectNumber} at {asAt:dd MMM yyyy}";
        var lines = new List<JournalLineInput>();
        AddAssetChange(lines, contractAssetAccountId, description, assetChange, project.Id);
        AddLiabilityChange(
            lines, contractLiabilityAccountId, description, liabilityChange, project.Id);
        lines.Add(movement > 0m
            ? new JournalLineInput(
                revenueAccountId, description, 0m, movement, ProjectId: project.Id)
            : new JournalLineInput(
                revenueAccountId, description, -movement, 0m, ProjectId: project.Id));

        var journal = await journals.PostAsync(
            userId,
            new JournalPostRequest(
                organisationId,
                asAt,
                $"WIP-{project.ProjectNumber}-{asAt:yyyyMMdd}",
                description,
                lines,
                project.BranchId,
                project.DivisionId),
            cancellationToken);
        var wipPosting = new ProjectWipPosting
        {
            OrganisationId = organisationId,
            ProjectId = project.Id,
            AsAt = asAt,
            PreviousWipAmount = previousWip,
            RequiredWipAmount = requiredWip,
            MovementAmount = movement,
            PostedJournalId = journal.Id,
            PostedByUserId = userId
        };
        db.ProjectWipPostings.Add(wipPosting);
        db.AuditEvents.Add(new AuditEvent
        {
            OrganisationId = organisationId,
            EventType = "ProjectWipPosted",
            EntityType = nameof(ProjectWipPosting),
            EntityId = wipPosting.Id.ToString(),
            UserId = userId,
            JsonData = JsonSerializer.Serialize(new
            {
                project.ProjectNumber,
                wipPosting.AsAt,
                wipPosting.PreviousWipAmount,
                wipPosting.RequiredWipAmount,
                wipPosting.MovementAmount,
                PostedJournalId = journal.Id,
                journal.SequenceNumber
            })
        });
        await db.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return wipPosting;
    }

    private static void AddAssetChange(
        List<JournalLineInput> lines,
        Guid accountId,
        string description,
        decimal change,
        Guid projectId)
    {
        if (change > 0m)
        {
            lines.Add(new(accountId, description, change, 0m, ProjectId: projectId));
        }
        else if (change < 0m)
        {
            lines.Add(new(accountId, description, 0m, -change, ProjectId: projectId));
        }
    }

    private static void AddLiabilityChange(
        List<JournalLineInput> lines,
        Guid accountId,
        string description,
        decimal change,
        Guid projectId)
    {
        if (change > 0m)
        {
            lines.Add(new(accountId, description, 0m, change, ProjectId: projectId));
        }
        else if (change < 0m)
        {
            lines.Add(new(accountId, description, -change, 0m, ProjectId: projectId));
        }
    }

    private static decimal Currency(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);
}

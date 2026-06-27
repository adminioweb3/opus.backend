namespace Citationly.Application.Interfaces;

public interface IScrapingJobService
{
    Task ProcessJobAsync(Guid jobId);
}

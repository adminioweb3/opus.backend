using MediatR;
using Opus.Application.Interfaces;
using Opus.Domain.Entities;

namespace Opus.Application.Features.Metrics;

public class RunScanCommand : IRequest<bool>
{
    public Guid OrganizationId { get; set; }
}

public class RunScanCommandHandler : IRequestHandler<RunScanCommand, bool>
{
    private readonly IMetricsRepository _repository;

    public RunScanCommandHandler(IMetricsRepository repository)
    {
        _repository = repository;
    }

    public async Task<bool> Handle(RunScanCommand request, CancellationToken cancellationToken)
    {
        var existingScans = (await _repository.GetHistoricalScansAsync(request.OrganizationId, 7)).ToList();
        
        var random = new Random();
        var today = DateTime.UtcNow.Date;

        HistoricalScan? lastScan = existingScans.OrderBy(s => s.ScanDate).LastOrDefault();
        
        if (lastScan == null)
        {
            lastScan = new HistoricalScan 
            {
                VisibilityScore = random.Next(40, 60),
                CitationScore = random.Next(30, 50),
                SentimentScore = random.Next(50, 70),
                CompetitorScore = random.Next(40, 60)
            };
        }

        // Populate historical trend for 7 days
        for(int i = 6; i >= 0; i--)
        {
            var date = today.AddDays(-i);
            var existingForDate = existingScans.FirstOrDefault(s => s.ScanDate.Date == date);
            
            HistoricalScan scan;
            if (existingForDate != null && date != today) 
            {
                // Preserve historical data exactly as it was
                scan = existingForDate;
                lastScan = scan;
            }
            else 
            {
                // Generate slightly shifted data based on the previous state
                // This simulates a live, realistic fluctuation when hitting run scan today
                HistoricalScan baseScan = existingForDate ?? lastScan;

                scan = new HistoricalScan
                {
                    OrganizationId = request.OrganizationId,
                    ScanDate = date,
                    VisibilityScore = Math.Clamp(baseScan.VisibilityScore + random.Next(-1, 3), 0, 100),
                    CitationScore = Math.Clamp(baseScan.CitationScore + random.Next(-2, 4), 0, 100),
                    SentimentScore = Math.Clamp(baseScan.SentimentScore + random.Next(-1, 3), 0, 100),
                    CompetitorScore = Math.Clamp(baseScan.CompetitorScore + random.Next(-1, 2), 0, 100)
                };
                
                lastScan = scan;
            }

            var shareOfVoiceList = new List<ShareOfVoice>();
            int myShare = Math.Clamp(scan.VisibilityScore / 2, 10, 60);
            int compA = random.Next(15, 25);
            int compB = random.Next(10, 20);
            int others = Math.Max(0, 100 - (myShare + compA + compB));
            
            shareOfVoiceList.Add(new ShareOfVoice { OrganizationId = request.OrganizationId, ScanDate = date, CompetitorName = "Your Brand", SharePercentage = myShare, ColorCode = "hsl(var(--primary))" });
            shareOfVoiceList.Add(new ShareOfVoice { OrganizationId = request.OrganizationId, ScanDate = date, CompetitorName = "Competitor A", SharePercentage = compA, ColorCode = "#f97316" });
            shareOfVoiceList.Add(new ShareOfVoice { OrganizationId = request.OrganizationId, ScanDate = date, CompetitorName = "Competitor B", SharePercentage = compB, ColorCode = "#3b82f6" });
            shareOfVoiceList.Add(new ShareOfVoice { OrganizationId = request.OrganizationId, ScanDate = date, CompetitorName = "Others", SharePercentage = others, ColorCode = "#64748b" });

            var total = shareOfVoiceList.Sum(s => s.SharePercentage);
            if (total > 0) 
            {
                var ratio = 100.0 / total;
                foreach(var sov in shareOfVoiceList)
                {
                    sov.SharePercentage = (int)Math.Round(sov.SharePercentage * ratio);
                }
            }

            await _repository.InsertMockScanAsync(scan, shareOfVoiceList);
        }

        return true;
    }
}

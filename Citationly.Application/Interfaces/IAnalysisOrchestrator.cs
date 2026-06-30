using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Citationly.Application.Interfaces
{
    public interface IAnalysisOrchestrator
    {
        IAsyncEnumerable<string> ExecuteAnalysisStreamAsync(Guid organizationId, Guid? websiteId, CancellationToken cancellationToken);
    }
}

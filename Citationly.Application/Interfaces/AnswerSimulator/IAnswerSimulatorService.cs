using System.Threading.Tasks;
using Citationly.Application.Features.AnswerSimulator;

namespace Citationly.Application.Interfaces.AnswerSimulator;

public interface IAnswerSimulatorService
{
    Task<SimulateAnswerResponse> SimulateAsync(Guid organizationId, SimulateAnswerRequest request);
    Task<CompareContentResponse> CompareAsync(Guid organizationId, CompareContentRequest request);
    Task<BattleResponse> BattleAsync(Guid organizationId, BattleRequest request);
}

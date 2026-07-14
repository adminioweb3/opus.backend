using System.Threading.Tasks;
using Citationly.Application.Features.AnswerSimulator;

namespace Citationly.Application.Interfaces.AnswerSimulator;

public interface IAnswerSimulatorService
{
    Task<SimulateAnswerResponse> SimulateAsync(SimulateAnswerRequest request);
    Task<CompareContentResponse> CompareAsync(CompareContentRequest request);
    Task<BattleResponse> BattleAsync(BattleRequest request);
}

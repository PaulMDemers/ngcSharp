using NgcSharp.Core;
using NgcSharp.Cpu;

namespace NgcSharp.App;

public sealed record DolRunStep(
    int ExecutedInstructions,
    uint Pc,
    uint Instruction,
    PowerPcState State,
    GameCubeBus Bus,
    bool IsInitial = false,
    bool IsFinal = false);

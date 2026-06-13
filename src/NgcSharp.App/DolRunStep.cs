using NgcSharp.Core;
using NgcSharp.Cpu;

namespace NgcSharp.App;

public readonly record struct DolRunStep(
    int ExecutedInstructions,
    uint Pc,
    uint Instruction,
    PowerPcState State,
    GameCubeBus Bus,
    bool IsInitial = false,
    bool IsFinal = false);

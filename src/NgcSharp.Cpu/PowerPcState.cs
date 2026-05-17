namespace NgcSharp.Cpu;

public sealed class PowerPcState
{
    public uint[] Gpr { get; } = new uint[32];

    public double[] Fpr { get; } = new double[32];

    public double[] FprPair1 { get; } = new double[32];

    public uint Pc { get; set; }

    public uint Lr { get; set; }

    public uint Ctr { get; set; }

    public uint Cr { get; set; }

    public uint Xer { get; set; }

    public uint Fpscr { get; set; }

    public uint Msr { get; set; }

    public ulong TimeBase { get; set; }

    public uint[] Spr { get; } = new uint[1024];

    public uint[] SegmentRegisters { get; } = new uint[16];

    public bool Halted { get; set; }

    public bool HasReservation { get; set; }

    public uint ReservationAddress { get; set; }

    public PowerPcState Clone()
    {
        PowerPcState clone = new()
        {
            Pc = Pc,
            Lr = Lr,
            Ctr = Ctr,
            Cr = Cr,
            Xer = Xer,
            Fpscr = Fpscr,
            Msr = Msr,
            TimeBase = TimeBase,
            Halted = Halted,
            HasReservation = HasReservation,
            ReservationAddress = ReservationAddress,
        };

        Array.Copy(Gpr, clone.Gpr, Gpr.Length);
        Array.Copy(Fpr, clone.Fpr, Fpr.Length);
        Array.Copy(FprPair1, clone.FprPair1, FprPair1.Length);
        Array.Copy(Spr, clone.Spr, Spr.Length);
        Array.Copy(SegmentRegisters, clone.SegmentRegisters, SegmentRegisters.Length);
        return clone;
    }
}

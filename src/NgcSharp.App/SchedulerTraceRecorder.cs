using NgcSharp.Core;
using NgcSharp.Cpu;

namespace NgcSharp.App;

public sealed class SchedulerTraceRecorder
{
    private const uint ThreadQueueHeadPointer = 0x8000_00C0;
    private const uint DefaultThreadPointer = 0x8000_00D8;
    private const uint CurrentThread0Pointer = 0x8000_00E0;
    private const uint CurrentThread4Pointer = 0x8000_00E4;
    private const int ThreadRecordSize = 0x320;

    private static readonly Dictionary<uint, string> LowMemoryPointers = new()
    {
        [ThreadQueueHeadPointer] = "thread_queue_head",
        [DefaultThreadPointer] = "default_thread",
        [CurrentThread0Pointer] = "current_thread_0",
        [CurrentThread4Pointer] = "current_thread_4",
    };

    private static readonly Dictionary<uint, string> ThreadFields = new()
    {
        [0x004] = "sp",
        [0x084] = "lr",
        [0x198] = "srr0",
        [0x19C] = "srr1",
        [0x1A2] = "flags",
        [0x2C8] = "state",
        [0x2CA] = "attr",
        [0x2CC] = "suspend_or_state",
        [0x2D0] = "priority",
        [0x2D4] = "base_priority",
        [0x2D8] = "val",
        [0x2DC] = "wait_queue",
        [0x2E0] = "queue_next",
        [0x2E4] = "queue_prev",
        [0x2E8] = "thread_next",
        [0x2EC] = "thread_prev",
    };

    private static readonly Dictionary<uint, string> MessageQueueFields = new()
    {
        [0x00] = "send_head",
        [0x04] = "send_tail",
        [0x08] = "recv_head",
        [0x0C] = "recv_tail",
        [0x10] = "buffer",
        [0x14] = "capacity",
        [0x18] = "read_index",
        [0x1C] = "used",
    };

    private readonly TextWriter _writer;
    private readonly GameCubeBus _bus;
    private readonly HashSet<uint> _knownThreads = [];
    private readonly HashSet<uint> _knownQueues = [];
    private ulong _stores;

    public SchedulerTraceRecorder(TextWriter writer, GameCubeBus bus)
    {
        _writer = writer;
        _bus = bus;
        _writer.WriteLine("instruction,pc,opcode,disassembly,kind,address,width,value,detail,current_thread,ready_bits,schedule_flag");
        RefreshKnownObjects();
    }

    public void RecordStore(int instructionIndex, uint pc, uint opcode, PowerPcState state, uint address, int width, uint value)
    {
        _stores++;
        if ((_stores & 0xFFF) == 0)
        {
            RefreshKnownObjects();
        }

        if (!TryNormalizeMainRamAddress(address, out uint normalizedAddress))
        {
            return;
        }

        if (LowMemoryPointers.TryGetValue(normalizedAddress, out string? pointerName))
        {
            if (TryNormalizeMainRamAddress(value, out uint pointedThread))
            {
                AddThread(pointedThread);
            }

            Write(instructionIndex, pc, opcode, "lowmem", normalizedAddress, width, value, pointerName);
            return;
        }

        if (TryDescribeSchedulerGlobal(normalizedAddress, value, out string? schedulerGlobal))
        {
            Write(instructionIndex, pc, opcode, "scheduler", normalizedAddress, width, value, schedulerGlobal);
            return;
        }

        if (TryDescribeKnownThreadField(normalizedAddress, value, out string? threadField))
        {
            Write(instructionIndex, pc, opcode, "thread", normalizedAddress, width, value, threadField);
            return;
        }

        if (TryDescribeKnownQueueField(normalizedAddress, out string? queueField))
        {
            Write(instructionIndex, pc, opcode, "message_queue", normalizedAddress, width, value, queueField);
        }
    }

    public void RecordBulkWrite(int instructionIndex, uint pc, uint opcode, uint address, int length)
    {
        if (!TryNormalizeMainRamAddress(address, out uint normalizedAddress))
        {
            return;
        }

        foreach (uint thread in _knownThreads)
        {
            if (RangesOverlap(normalizedAddress, (uint)length, thread, ThreadRecordSize))
            {
                Write(instructionIndex, pc, opcode, "bulk", normalizedAddress, length, 0, $"thread_range base=0x{thread:X8}");
                return;
            }
        }

        foreach (uint queue in _knownQueues)
        {
            if (RangesOverlap(normalizedAddress, (uint)length, queue, 0x20))
            {
                Write(instructionIndex, pc, opcode, "bulk", normalizedAddress, length, 0, $"message_queue_range base=0x{queue:X8}");
                return;
            }
        }
    }

    private void RefreshKnownObjects()
    {
        AddThreadPointer(ThreadQueueHeadPointer, followThreadChain: true);
        AddThreadPointer(DefaultThreadPointer, followThreadChain: false);
        AddThreadPointer(CurrentThread0Pointer, followThreadChain: true);
        AddThreadPointer(CurrentThread4Pointer, followThreadChain: true);
    }

    private void AddThreadPointer(uint pointerAddress, bool followThreadChain)
    {
        try
        {
            uint rawThread = _bus.Memory.Read32(pointerAddress);
            if (!TryNormalizeMainRamAddress(rawThread, out uint threadAddress))
            {
                return;
            }

            AddThread(threadAddress);
            if (followThreadChain)
            {
                AddThreadChain(threadAddress);
            }
        }
        catch (AddressTranslationException)
        {
        }
    }

    private void AddThreadChain(uint startThread)
    {
        HashSet<uint> seen = [];
        uint thread = startThread;
        for (int index = 0; index < 32 && thread != 0 && seen.Add(thread); index++)
        {
            AddThread(thread);
            try
            {
                uint next = _bus.Memory.Read32(thread + 0x2E0);
                if (!TryNormalizeMainRamAddress(next, out thread))
                {
                    break;
                }
            }
            catch (AddressTranslationException)
            {
                break;
            }
        }
    }

    private void AddThread(uint thread)
    {
        if (!IsMainRamRange(thread, ThreadRecordSize))
        {
            return;
        }

        _knownThreads.Add(thread);

        try
        {
            uint waitQueue = _bus.Memory.Read32(thread + 0x2DC);
            AddWaitQueue(waitQueue);
        }
        catch (AddressTranslationException)
        {
        }
    }

    private void AddWaitQueue(uint waitQueue)
    {
        if (!TryNormalizeMainRamAddress(waitQueue, out uint normalizedWaitQueue))
        {
            return;
        }

        if (IsMainRamRange(normalizedWaitQueue, 0x20))
        {
            _knownQueues.Add(normalizedWaitQueue);
        }

        if (normalizedWaitQueue >= GameCubeAddress.MainRamCachedStart + 8 && IsMainRamRange(normalizedWaitQueue - 8, 0x20))
        {
            _knownQueues.Add(normalizedWaitQueue - 8);
        }
    }

    private bool TryDescribeSchedulerGlobal(uint address, uint value, out string detail)
    {
        detail = string.Empty;
        uint r13 = _bus.SmallDataBaseRegister;
        if (!TryNormalizeMainRamAddress(r13, out uint normalizedR13))
        {
            return false;
        }

        uint readyBits = unchecked(normalizedR13 - 30176u);
        uint scheduleFlag = unchecked(normalizedR13 - 30172u);
        if (address == readyBits)
        {
            detail = $"ready_bits r13=0x{normalizedR13:X8} value=0x{value:X8}";
            return true;
        }

        if (address == scheduleFlag)
        {
            detail = $"schedule_flag r13=0x{normalizedR13:X8} value=0x{value:X8}";
            return true;
        }

        return false;
    }

    private bool TryDescribeKnownThreadField(uint address, uint value, out string detail)
    {
        detail = string.Empty;
        foreach (uint thread in _knownThreads)
        {
            if (address < thread || address >= thread + ThreadRecordSize)
            {
                continue;
            }

            uint offset = address - thread;
            if (!ThreadFields.TryGetValue(offset, out string? fieldName))
            {
                return false;
            }

            if (offset == 0x2DC)
            {
                AddWaitQueue(value);
            }

            detail = $"thread=0x{thread:X8} field={fieldName} offset=0x{offset:X3} {DescribeThread(thread)}";
            return true;
        }

        return false;
    }

    private bool TryDescribeKnownQueueField(uint address, out string detail)
    {
        detail = string.Empty;
        foreach (uint queue in _knownQueues)
        {
            if (address < queue || address >= queue + 0x20)
            {
                continue;
            }

            uint offset = address - queue;
            if (!MessageQueueFields.TryGetValue(offset, out string? fieldName))
            {
                return false;
            }

            detail = $"queue=0x{queue:X8} field={fieldName} offset=0x{offset:X2} {DescribeQueue(queue)}";
            return true;
        }

        return false;
    }

    private string DescribeThread(uint thread)
    {
        try
        {
            ushort state = _bus.Memory.Read16(thread + 0x2C8);
            ushort flags = _bus.Memory.Read16(thread + 0x1A2);
            uint priority = _bus.Memory.Read32(thread + 0x2D0);
            uint waitQueue = _bus.Memory.Read32(thread + 0x2DC);
            uint queueNext = _bus.Memory.Read32(thread + 0x2E0);
            uint queuePrev = _bus.Memory.Read32(thread + 0x2E4);
            uint srr0 = _bus.Memory.Read32(thread + 0x198);
            return $"state=0x{state:X4} flags=0x{flags:X4} priority=0x{priority:X8} waitq=0x{waitQueue:X8} qnext=0x{queueNext:X8} qprev=0x{queuePrev:X8} srr0=0x{srr0:X8}";
        }
        catch (AddressTranslationException)
        {
            return "thread_record_truncated";
        }
    }

    private string DescribeQueue(uint queue)
    {
        try
        {
            uint sendHead = _bus.Memory.Read32(queue);
            uint sendTail = _bus.Memory.Read32(queue + 4);
            uint recvHead = _bus.Memory.Read32(queue + 8);
            uint recvTail = _bus.Memory.Read32(queue + 12);
            uint buffer = _bus.Memory.Read32(queue + 16);
            uint capacity = _bus.Memory.Read32(queue + 20);
            uint readIndex = _bus.Memory.Read32(queue + 24);
            uint used = _bus.Memory.Read32(queue + 28);
            return $"send=0x{sendHead:X8}/0x{sendTail:X8} recv=0x{recvHead:X8}/0x{recvTail:X8} buffer=0x{buffer:X8} capacity={capacity} read={readIndex} used={used}";
        }
        catch (AddressTranslationException)
        {
            return "queue_record_truncated";
        }
    }

    private void Write(int instructionIndex, uint pc, uint opcode, string kind, uint address, int width, uint value, string detail)
    {
        string disassembly = PowerPcDisassembler.Disassemble(opcode).Replace("\"", "\"\"", StringComparison.Ordinal);
        uint currentThread = Read32OrZero(CurrentThread4Pointer);
        uint readyBits = 0;
        uint scheduleFlag = 0;
        if (TryNormalizeMainRamAddress(_bus.SmallDataBaseRegister, out uint r13))
        {
            readyBits = Read32OrZero(unchecked(r13 - 30176u));
            scheduleFlag = Read32OrZero(unchecked(r13 - 30172u));
        }

        _writer.WriteLine($"{instructionIndex},0x{pc:X8},0x{opcode:X8},\"{disassembly}\",{kind},0x{address:X8},{width},0x{value:X8},\"{detail.Replace("\"", "\"\"", StringComparison.Ordinal)}\",0x{currentThread:X8},0x{readyBits:X8},0x{scheduleFlag:X8}");
    }

    private uint Read32OrZero(uint address)
    {
        try
        {
            return _bus.Memory.Read32(address);
        }
        catch (AddressTranslationException)
        {
            return 0;
        }
    }

    private static bool RangesOverlap(uint startA, uint lengthA, uint startB, uint lengthB)
    {
        ulong endA = (ulong)startA + lengthA;
        ulong endB = (ulong)startB + lengthB;
        return startA < endB && startB < endA;
    }

    private static bool IsMainRamRange(uint address, int length)
    {
        if (!TryNormalizeMainRamAddress(address, out uint normalized))
        {
            return false;
        }

        if (length < 0 || length > GameCubeAddress.MainRamSize)
        {
            return false;
        }

        uint offset = normalized - GameCubeAddress.MainRamCachedStart;
        return offset <= GameCubeAddress.MainRamSize - (uint)length;
    }

    private static bool TryNormalizeMainRamAddress(uint address, out uint normalized)
    {
        if (!GameCubeAddress.TryTranslateMainRam(address, out int offset))
        {
            normalized = 0;
            return false;
        }

        normalized = GameCubeAddress.MainRamCachedStart + (uint)offset;
        return true;
    }
}

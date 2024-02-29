using System;
using System.Collections;
using System.Collections.Generic;
using VisualGDBExtensibility;

namespace ChibiOsThreads
{
    public class ChibiOsThreadProvider : IVirtualThreadProvider2
    {
        private const int CONTEXT_OFFSET = 10;
        private const uint SCB_ICSR_VECTACTIVE_Msk = 0x1FF;

        public IVirtualThread[] GetVirtualThreads(IGlobalExpressionEvaluator e)
        {
            ulong? ch = e.EvaluateIntegralExpression("&ch");
            ulong? firstThread = e.EvaluateIntegralExpression("ch.rlist.newer");

            if (!firstThread.HasValue)
                return Array.Empty<IVirtualThread>();

            // SCB->ICSR
            ulong icsr = e.EvaluateIntegralExpression("*(uint32_t*)0xe000ed04") ?? 0;
            bool isInInterrupt = (icsr & SCB_ICSR_VECTACTIVE_Msk) > 0;

            ulong? currentThread = e.EvaluateIntegralExpression("ch.rlist.current");

            if (!currentThread.HasValue)
                return Array.Empty<IVirtualThread>();

            List<IVirtualThread> threads = new List<IVirtualThread>();

            ulong? thread = firstThread;
            do
            {
                var virtualThread = new VirtualThread(thread.Value, threads.Count + 1, thread == currentThread, isInInterrupt, e);
                threads.Add(virtualThread);
                thread = e.EvaluateIntegralExpression($"((ch_thread *)0x{thread.Value:x}).newer");
            } while (thread.HasValue && thread.Value != ch);

            return threads.ToArray();
        }

        public void SetConfiguration(Dictionary<string, string> savedConfiguration)
        {
            // Implementation for setting configuration
        }

        public int? GetActiveVirtualThreadId(IGlobalExpressionEvaluator e)
        {
            ulong? firstThread = e.EvaluateIntegralExpression("ch.rlist.newer");
            if (!firstThread.HasValue)
                return null;

            ulong? currentThread = e.EvaluateIntegralExpression("ch.rlist.current");
            if (!currentThread.HasValue)
                return null;

            ulong? thread = firstThread;
            int index = 1;

            do
            {
                if (thread == currentThread)
                    return index;

                thread = e.EvaluateIntegralExpression($"((ch_thread *)0x{thread.Value:x}).newer");
                index++;
            } while (thread.HasValue && thread.Value != firstThread);

            return null;
        }

        public IEnumerable CreateToolbarContents() => null;

        public Dictionary<string, string> GetConfigurationIfChanged() => null;

        private class VirtualThread : IVirtualThread
        {
            private readonly ulong _threadObjectAddress;
            private readonly string _name;
            private readonly ulong _sp;
            private readonly ulong _lr;
            private readonly int _index;
            private readonly IGlobalExpressionEvaluator _evaluator;
            private readonly bool _running;

            public VirtualThread(ulong threadObjectAddress, int index, bool running, bool isInInterrupt, IGlobalExpressionEvaluator evaluator)
            {
                _threadObjectAddress = threadObjectAddress;
                _evaluator = evaluator;
                _running = running;
                UniqueID = index;

                _name = evaluator.EvaluateStringExpression($"((ch_thread *)0x{_threadObjectAddress:x}).name");
                _sp = evaluator.EvaluateIntegralExpression($"((ch_thread *)0x{_threadObjectAddress:x}).ctx.sp") ?? 0;
                _lr = evaluator.EvaluateIntegralExpression($"((ch_thread *)0x{_threadObjectAddress:x}).ctx.sp.lr") ?? 0;

                if (isInInterrupt && running)
                {
                    _name = "(INT) " + _name;
                }
            }

            public int UniqueID { get; }

            public IEnumerable<KeyValuePair<string, ulong>> GetSavedRegisters()
            {
                if (_running)
                    return null;

                var result = new List<KeyValuePair<string, ulong>>();

                for (int i = 4; i < 13; i++)
                {
                    ulong val = GetSavedRegister(i);
                    result.Add(new KeyValuePair<string, ulong>($"r{i}", val));
                }

                result.Add(new KeyValuePair<string, ulong>("r13", _sp));
                result.Add(new KeyValuePair<string, ulong>("r14", GetSavedRegister(13)));

                ulong? pc = _evaluator.EvaluateIntegralExpression("_port_switch");
                result.Add(new KeyValuePair<string, ulong>("r15", pc.Value + CONTEXT_OFFSET));

                return result;
            }

            private ulong GetSavedRegister(int slotNumber)
            {
                ulong? reg = _evaluator.EvaluateIntegralExpression($"((void **)0x{_sp:x})[{slotNumber - 4}]");
                return reg.GetValueOrDefault(0);
            }

            public bool IsCurrentlyExecuting => _running;

            public string Name => _name;

            public bool IsValid => true;
        }
    }
}

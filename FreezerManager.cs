namespace MemTools
{
    public class FreezerManager
    {
        private readonly Dictionary<IntPtr, (object value, Type type)> Values = new();
        private readonly Memory Mem;
        private Thread FreezeThread;
        private bool Running;

        public FreezerManager(Memory mem)
        {
            Mem = mem;
        }

        public void Add<T>(IntPtr address, T value) where T : unmanaged
        {
            if(Values.ContainsKey(address))
                return;

            Values[address] = (value, typeof(T));
        }

        public void Remove(IntPtr address)
        {
            Values.Remove(address);
        }

        public void Start(int intervalMs = 50)
        {
            if(Running) return;
            Running = true;

            FreezeThread = new Thread(() =>
            {
                while(Running)
                {
                    foreach(KeyValuePair<IntPtr, (object value, Type type)> kvp in Values)
                    {
                        IntPtr addr = kvp.Key;
                        (object value, Type type) = kvp.Value;

                        if(type == typeof(int))
                            Mem.Write(addr, (int)value);
                        else if(type == typeof(float))
                            Mem.Write(addr, (float)value);
                        else if(type == typeof(double))
                            Mem.Write(addr, (double)value);
                    }

                    Thread.Sleep(intervalMs);
                }
            })
            { IsBackground = true };

            FreezeThread.Start();
        }

        public void Stop()
        {
            Running = false;
            FreezeThread?.Join();
        }
    }
}

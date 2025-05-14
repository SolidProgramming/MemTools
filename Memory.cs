using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace MemTools
{
    public class Memory
    {
        private IntPtr processHandle;

        [DllImport("kernel32.dll")]
        private static extern IntPtr OpenProcess(int dwDesiredAccess, bool bInheritHandle, int dwProcessId);

        [DllImport("kernel32.dll")]
        private static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, int dwSize, out IntPtr lpNumberOfBytesRead);

        [DllImport("kernel32.dll")]
        private static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, int dwSize, out IntPtr lpNumberOfBytesWritten);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr hObject);

        private const int PROCESS_ALL_ACCESS = 0x1F0FFF;

        public Memory(Process process)
        {
            processHandle = OpenProcess(PROCESS_ALL_ACCESS, false, process.Id);
            if(processHandle == IntPtr.Zero)
                throw new Exception("Prozess konnte nicht geöffnet werden.");
        }

        public void Close()
        {
            if(processHandle != IntPtr.Zero)
            {
                CloseHandle(processHandle);
                processHandle = IntPtr.Zero;
            }
        }

        public byte[] ReadMemory(IntPtr address, int size)
        {
            byte[] buffer = new byte[size];
            ReadProcessMemory(processHandle, address, buffer, size, out _);
            return buffer;
        }

        public void WriteMemory(IntPtr address, byte[] data)
        {
            WriteProcessMemory(processHandle, address, data, data.Length, out _);
        }

        public IntPtr FollowPointerChain(IntPtr baseAddress, int[] offsets)
        {
            IntPtr current = baseAddress;
            for(int i = 0; i < offsets.Length; i++)
            {
                current = Read<IntPtr>(current);
                current += offsets[i];
            }
            return current;
        }

        public T Read<T>(IntPtr address) where T : unmanaged
        {
            int size = Unsafe.SizeOf<T>();
            byte[] buffer = ReadMemory(address, size);

            unsafe
            {
                fixed(byte* ptr = buffer)
                {
                    return *(T*)ptr;
                }
            }
        }

        public void Write<T>(IntPtr address, T value) where T : unmanaged
        {
            int size = Unsafe.SizeOf<T>();
            byte[] buffer = new byte[size];

            unsafe
            {
                fixed(byte* ptr = buffer)
                {
                    *(T*)ptr = value;
                }
            }

            WriteMemory(address, buffer);
        }

        public T ReadStruct<T>(IntPtr address) where T : struct
        {
            int size = Marshal.SizeOf<T>();
            byte[] buffer = ReadMemory(address, size);

            GCHandle handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            T obj = Marshal.PtrToStructure<T>(handle.AddrOfPinnedObject());
            handle.Free();

            return obj;
        }

        public void WriteStruct<T>(IntPtr address, T value) where T : struct
        {
            int size = Marshal.SizeOf<T>();
            byte[] buffer = new byte[size];

            IntPtr ptr = Marshal.AllocHGlobal(size);
            Marshal.StructureToPtr(value, ptr, false);
            Marshal.Copy(ptr, buffer, 0, size);
            Marshal.FreeHGlobal(ptr);

            WriteMemory(address, buffer);
        }

        public string ReadString(IntPtr address, int maxLength, bool isUnicode = false)
        {
            byte[] buffer = ReadMemory(address, maxLength);

            string result = isUnicode
                ? System.Text.Encoding.Unicode.GetString(buffer)
                : System.Text.Encoding.UTF8.GetString(buffer);

            int nullIndex = result.IndexOf('\0');
            return nullIndex >= 0 ? result.Substring(0, nullIndex) : result;
        }

        public void WriteString(IntPtr address, string text, bool isUnicode = false, bool zeroTerminate = true)
        {
            Encoding encoding = isUnicode ? System.Text.Encoding.Unicode : System.Text.Encoding.UTF8;
            byte[] strBytes = encoding.GetBytes(text);

            if(zeroTerminate)
            {
                Array.Resize(ref strBytes, strBytes.Length + (isUnicode ? 2 : 1));
            }

            WriteMemory(address, strBytes);
        }

        public T[] ReadArray<T>(IntPtr address, int count) where T : unmanaged
        {
            int size = Unsafe.SizeOf<T>();
            byte[] buffer = ReadMemory(address, size * count);
            T[] result = new T[count];

            unsafe
            {
                fixed(byte* ptr = buffer)
                fixed(T* outPtr = result)
                {
                    Buffer.MemoryCopy(ptr, outPtr, buffer.Length, buffer.Length);
                }
            }

            return result;
        }

        public void WriteArray<T>(IntPtr address, T[] values) where T : unmanaged
        {
            int size = Unsafe.SizeOf<T>();
            byte[] buffer = new byte[size * values.Length];

            unsafe
            {
                fixed(byte* ptr = buffer)
                fixed(T* inPtr = values)
                {
                    Buffer.MemoryCopy(inPtr, ptr, buffer.Length, buffer.Length);
                }
            }

            WriteMemory(address, buffer);
        }

        public static IntPtr GetModuleBaseAddress(Process process, string moduleName)
        {
            foreach(ProcessModule module in process.Modules)
            {
                if(string.Equals(module.ModuleName, moduleName, StringComparison.OrdinalIgnoreCase))
                {
                    return module.BaseAddress;
                }
            }
            return IntPtr.Zero;
        }

        public List<IntPtr> ScanForValue<T>(T value, IntPtr startAddress, long size) where T : unmanaged, IEquatable<T>
        {
            List<IntPtr> results = new();
            int typeSize = Unsafe.SizeOf<T>();
            byte[] buffer = ReadMemory(startAddress, (int)size);

            unsafe
            {
                fixed(byte* ptr = buffer)
                {
                    for(long i = 0; i < size - typeSize; i++)
                    {
                        T current = *(T*)(ptr + i);
                        if(current.Equals(value))
                        {
                            results.Add(startAddress + (int)i);
                        }
                    }
                }
            }

            return results;
        }

        private static byte?[] ParsePattern(string pattern)
        {
            return pattern.Split(' ').Select(b => b == "??" ? (byte?)null : Convert.ToByte(b, 16)).ToArray();
        }

        public IntPtr PatternScan(IntPtr start, int size, string pattern)
        {
            byte[] buffer = ReadMemory(start, size);
            byte?[] patternBytes = ParsePattern(pattern);

            for(int i = 0; i <= buffer.Length - patternBytes.Length; i++)
            {
                bool match = true;

                for(int j = 0; j < patternBytes.Length; j++)
                {
                    if(patternBytes[j].HasValue && buffer[i + j] != patternBytes[j])
                    {
                        match = false;
                        break;
                    }
                }

                if(match)
                {
                    return start + i;
                }
            }

            return IntPtr.Zero;
        }

    }
}

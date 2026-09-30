using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Text;

namespace vibeRacingOverlays.Data.IRSdk
{
    public enum VarType { Char = 0, Bool = 1, Int = 2, BitField = 3, Float = 4, Double = 5 }

    public sealed class VarHeader
    {
        public string Name;
        public VarType Type;
        public int Offset;
        public int Count;
        public string Unit;
        public string Desc;
    }

    /// <summary>
    /// One copy of the telemetry buffer (60 Hz) with typed accessors by variable name.
    /// </summary>
    public sealed class RawFrame
    {
        readonly byte[] buf;
        readonly Dictionary<string, VarHeader> vars;
        public readonly int TickCount;

        internal RawFrame(byte[] buf, Dictionary<string, VarHeader> vars, int tick)
        {
            this.buf = buf; this.vars = vars; TickCount = tick;
        }

        public bool Has(string name) { return vars.ContainsKey(name); }

        public float Float(string name, float def = 0)
        {
            VarHeader v;
            if (!vars.TryGetValue(name, out v)) return def;
            switch (v.Type)
            {
                case VarType.Float: return BitConverter.ToSingle(buf, v.Offset);
                case VarType.Double: return (float)BitConverter.ToDouble(buf, v.Offset);
                case VarType.Int:
                case VarType.BitField: return BitConverter.ToInt32(buf, v.Offset);
                case VarType.Bool: return buf[v.Offset];
                default: return def;
            }
        }

        public double Double(string name, double def = 0)
        {
            VarHeader v;
            if (!vars.TryGetValue(name, out v)) return def;
            return v.Type == VarType.Double ? BitConverter.ToDouble(buf, v.Offset) : Float(name, (float)def);
        }

        public int Int(string name, int def = 0)
        {
            VarHeader v;
            if (!vars.TryGetValue(name, out v)) return def;
            switch (v.Type)
            {
                case VarType.Int:
                case VarType.BitField: return BitConverter.ToInt32(buf, v.Offset);
                case VarType.Bool:
                case VarType.Char: return buf[v.Offset];
                case VarType.Float: return (int)BitConverter.ToSingle(buf, v.Offset);
                case VarType.Double: return (int)BitConverter.ToDouble(buf, v.Offset);
                default: return def;
            }
        }

        public bool Bool(string name) { return Int(name) != 0; }

        public float[] FloatArray(string name, int count)
        {
            var res = new float[count];
            VarHeader v;
            if (!vars.TryGetValue(name, out v)) return res;
            int n = Math.Min(count, v.Count);
            for (int i = 0; i < n; i++)
                res[i] = v.Type == VarType.Double ? (float)BitConverter.ToDouble(buf, v.Offset + i * 8) : BitConverter.ToSingle(buf, v.Offset + i * 4);
            return res;
        }

        public int[] IntArray(string name, int count)
        {
            var res = new int[count];
            VarHeader v;
            if (!vars.TryGetValue(name, out v)) return res;
            int n = Math.Min(count, v.Count);
            for (int i = 0; i < n; i++)
            {
                if (v.Type == VarType.Bool || v.Type == VarType.Char) res[i] = buf[v.Offset + i];
                else res[i] = BitConverter.ToInt32(buf, v.Offset + i * 4);
            }
            return res;
        }

        public bool[] BoolArray(string name, int count)
        {
            var res = new bool[count];
            VarHeader v;
            if (!vars.TryGetValue(name, out v)) return res;
            int n = Math.Min(count, v.Count);
            for (int i = 0; i < n; i++) res[i] = buf[v.Offset + i] != 0;
            return res;
        }
    }

    /// <summary>
    /// Reads iRacing's shared memory (Local\IRSDKMemMapFileName). Layout per irsdk_defines.h.
    /// </summary>
    public sealed class IRacingSdk : IDisposable
    {
        const string MemMapName = "Local\\IRSDKMemMapFileName";
        const string DataValidEventName = "Local\\IRSDKDataValidEvent";
        const int StatusConnected = 1;
        const int HeaderSize = 112;
        const int VarHeaderSize = 144;

        MemoryMappedFile mmf;
        MemoryMappedViewAccessor view;
        EventWaitHandle dataValid;
        Dictionary<string, VarHeader> vars;
        int varHeaderOffset, numVars;
        int lastTick = -1;
        int lastSessionUpdate = -1;

        static readonly Encoding Latin1 = Encoding.Latin1;

        public bool IsOpen { get { return view != null; } }

        public bool TryOpen()
        {
            if (view != null) return true;
            try
            {
                mmf = MemoryMappedFile.OpenExisting(MemMapName, MemoryMappedFileRights.Read);
                view = mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
                try { dataValid = EventWaitHandle.OpenExisting(DataValidEventName); } catch { dataValid = null; }
                vars = null;
                lastTick = -1; lastSessionUpdate = -1;
                return true;
            }
            catch
            {
                Close();
                return false;
            }
        }

        public void Close()
        {
            if (view != null) view.Dispose();
            if (mmf != null) mmf.Dispose();
            if (dataValid != null) dataValid.Dispose();
            view = null; mmf = null; dataValid = null; vars = null;
        }

        public void Dispose() { Close(); }

        public bool IsConnected
        {
            get { return view != null && (view.ReadInt32(4) & StatusConnected) != 0; }
        }

        /// <summary>Blocks until iRacing signals a new frame (or the timeout elapses).</summary>
        public void WaitForData(int timeoutMs)
        {
            if (dataValid != null) dataValid.WaitOne(timeoutMs);
            else Thread.Sleep(Math.Min(timeoutMs, 16));
        }

        /// <summary>Returns the newest telemetry buffer, or null when nothing new is available.</summary>
        public RawFrame ReadFrame()
        {
            if (!IsConnected) return null;

            int nv = view.ReadInt32(24);
            int vho = view.ReadInt32(28);
            if (vars == null || nv != numVars || vho != varHeaderOffset) ReadVarHeaders(nv, vho);

            int numBuf = view.ReadInt32(32);
            int bufLen = view.ReadInt32(36);

            // newest buffer = highest tick count
            int best = 0, bestTick = int.MinValue;
            for (int i = 0; i < numBuf && i < 4; i++)
            {
                int tick = view.ReadInt32(48 + i * 16);
                if (tick > bestTick) { bestTick = tick; best = i; }
            }
            if (bestTick == lastTick) return null;

            for (int attempt = 0; attempt < 2; attempt++)
            {
                int tick = view.ReadInt32(48 + best * 16);
                int offset = view.ReadInt32(48 + best * 16 + 4);
                var data = new byte[bufLen];
                view.ReadArray(offset, data, 0, bufLen);
                // make sure iRacing didn't overwrite the buffer while we copied it
                if (tick == view.ReadInt32(48 + best * 16))
                {
                    lastTick = tick;
                    return new RawFrame(data, vars, tick);
                }
            }
            return null;
        }

        /// <summary>Makes the next ReadSessionInfoIfChanged return the session YAML even if it did not change.</summary>
        public void ForceSessionReread() { lastSessionUpdate = -1; }

        /// <summary>Returns the session YAML when it changed since the last call, otherwise null.</summary>
        public string ReadSessionInfoIfChanged()
        {
            if (!IsConnected) return null;
            int update = view.ReadInt32(12);
            if (update == lastSessionUpdate) return null;
            int len = view.ReadInt32(16);
            int offset = view.ReadInt32(20);
            var bytes = new byte[len];
            view.ReadArray(offset, bytes, 0, len);
            int end = Array.IndexOf(bytes, (byte)0);
            if (end < 0) end = len;
            lastSessionUpdate = update;
            return Latin1.GetString(bytes, 0, end);
        }

        void ReadVarHeaders(int nv, int vho)
        {
            var dict = new Dictionary<string, VarHeader>(nv);
            var raw = new byte[VarHeaderSize];
            for (int i = 0; i < nv; i++)
            {
                view.ReadArray(vho + i * VarHeaderSize, raw, 0, VarHeaderSize);
                var h = new VarHeader
                {
                    Type = (VarType)BitConverter.ToInt32(raw, 0),
                    Offset = BitConverter.ToInt32(raw, 4),
                    Count = BitConverter.ToInt32(raw, 8),
                    Name = CString(raw, 16, 32),
                    Desc = CString(raw, 48, 64),
                    Unit = CString(raw, 112, 32),
                };
                dict[h.Name] = h;
            }
            vars = dict; numVars = nv; varHeaderOffset = vho;
        }

        static string CString(byte[] b, int start, int max)
        {
            int end = start;
            while (end < start + max && b[end] != 0) end++;
            return Latin1.GetString(b, start, end - start);
        }

        public IReadOnlyCollection<VarHeader> Variables
        {
            get { return vars != null ? (IReadOnlyCollection<VarHeader>)vars.Values : Array.Empty<VarHeader>(); }
        }
    }
}

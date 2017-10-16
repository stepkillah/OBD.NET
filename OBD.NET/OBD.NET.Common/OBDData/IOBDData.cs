namespace OBD.NET.Common.OBDData
{
    public interface IOBDData
    {
        int PID { get; }

        void Load(string data);
    }
}

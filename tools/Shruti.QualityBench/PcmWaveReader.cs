namespace Shruti.QualityBench;

public static class PcmWaveReader
{
    public static byte[] Read(string path)
    {
        using var reader = new BinaryReader(File.OpenRead(path));
        if (new string(reader.ReadChars(4)) != "RIFF") throw new InvalidDataException("Expected a RIFF WAV file.");
        reader.ReadUInt32();
        if (new string(reader.ReadChars(4)) != "WAVE") throw new InvalidDataException("Expected WAVE format.");
        bool validFormat = false;
        byte[]? audio = null;
        while (reader.BaseStream.Position + 8 <= reader.BaseStream.Length)
        {
            string chunk = new(reader.ReadChars(4));
            uint length = reader.ReadUInt32();
            long end = reader.BaseStream.Position + length;
            if (end > reader.BaseStream.Length || length > 32_000 * 600) throw new InvalidDataException("Invalid or oversized WAV chunk.");
            if (chunk == "fmt ")
            {
                if (length < 16) throw new InvalidDataException("Invalid WAV format chunk.");
                ushort format = reader.ReadUInt16(), channels = reader.ReadUInt16();
                uint rate = reader.ReadUInt32(); reader.ReadUInt32(); reader.ReadUInt16();
                ushort bits = reader.ReadUInt16();
                validFormat = format == 1 && channels == 1 && rate == 16000 && bits == 16;
            }
            if (chunk == "data") audio = reader.ReadBytes(checked((int)length));
            reader.BaseStream.Position = end + (length % 2);
        }
        if (!validFormat || audio is null || audio.Length % 2 != 0) throw new InvalidDataException("Fixtures require mono PCM16 WAV at 16 kHz.");
        return audio;
    }
}

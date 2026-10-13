#:property PublishAot=false
// Generates the reviewer sample photos. Each PNG is a head-and-shoulders silhouette; a tEXt chunk is varied until
// the simulated quality score (first 4 bytes of SHA-256, big-endian, % 101 - see HashPhotoQualityScorer) lands
// in the wanted bucket.
using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

var dir = args[0];
Directory.CreateDirectory(dir);

int Score(byte[] b) => (int)(BinaryPrimitives.ReadUInt32BigEndian(SHA256.HashData(b)) % 101);

void Make(string name, Func<int, bool> want, Func<int, byte[]> build)
{
    for (var seed = 0; ; seed++)
    {
        var bytes = build(seed);
        var score = Score(bytes);
        if (!want(score)) continue;
        File.WriteAllBytes(Path.Combine(dir, name), bytes);
        Console.WriteLine($"{name,-24} {bytes.Length,8} bytes  score {score}");
        return;
    }
}

// Background and silhouette colours; noise adds texture (and keeps the full-size photos above 10 KB).
(byte R, byte G, byte B)[,] Portrait(int w, int h, (byte, byte, byte) bg, bool noise)
{
    var rnd = new Random(42);
    var px = new (byte, byte, byte)[w, h];
    double cx = w / 2.0, headY = h * 0.38, headR = w * 0.22;
    for (var y = 0; y < h; y++)
    for (var x = 0; x < w; x++)
    {
        var inHead = Math.Pow((x - cx) / headR, 2) + Math.Pow((y - headY) / (headR * 1.2), 2) <= 1;
        var inShoulders = y > h * 0.66 && Math.Pow((x - cx) / (w * 0.45), 2) + Math.Pow((y - h * 1.02) / (h * 0.36), 2) <= 1;
        var (r, g, b) = inHead ? ((byte)226, (byte)190, (byte)160) : inShoulders ? ((byte)52, (byte)72, (byte)110) : bg;
        var n = noise ? rnd.Next(-10, 11) : 0;
        px[x, y] = (Clamp(r + n), Clamp(g + n), Clamp(b + n));
    }
    return px;
}

static byte Clamp(int v) => (byte)Math.Clamp(v, 0, 255);

byte[] Png((byte R, byte G, byte B)[,] px, string comment)
{
    int w = px.GetLength(0), h = px.GetLength(1);
    var raw = new MemoryStream();
    for (var y = 0; y < h; y++)
    {
        raw.WriteByte(0); // filter: none
        for (var x = 0; x < w; x++) { raw.WriteByte(px[x, y].R); raw.WriteByte(px[x, y].G); raw.WriteByte(px[x, y].B); }
    }
    var idat = new MemoryStream();
    using (var z = new ZLibStream(idat, CompressionLevel.SmallestSize, leaveOpen: true)) raw.WriteTo(z);

    var ihdr = new byte[13];
    BinaryPrimitives.WriteInt32BigEndian(ihdr, w);
    BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), h);
    ihdr[8] = 8; ihdr[9] = 2; // 8-bit RGB

    var png = new MemoryStream();
    png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
    Chunk(png, "IHDR", ihdr);
    Chunk(png, "tEXt", Encoding.Latin1.GetBytes("Comment\0" + comment));
    Chunk(png, "IDAT", idat.ToArray());
    Chunk(png, "IEND", []);
    return png.ToArray();
}

void Chunk(Stream s, string type, byte[] data)
{
    Span<byte> len = stackalloc byte[4];
    BinaryPrimitives.WriteInt32BigEndian(len, data.Length);
    s.Write(len);
    var typeAndData = Encoding.ASCII.GetBytes(type).Concat(data).ToArray();
    s.Write(typeAndData);
    BinaryPrimitives.WriteUInt32BigEndian(len, Crc32(typeAndData));
    s.Write(len);
}

static uint Crc32(byte[] data)
{
    var crc = 0xFFFFFFFFu;
    foreach (var b in data)
    {
        crc ^= b;
        for (var k = 0; k < 8; k++) crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
    }
    return ~crc;
}

byte[] Bmp((byte R, byte G, byte B)[,] px, int seed)
{
    int w = px.GetLength(0), h = px.GetLength(1), stride = (w * 3 + 3) & ~3, size = 54 + stride * h;
    var b = new byte[size + 4]; // 4 trailing bytes carry the seed
    b[0] = (byte)'B'; b[1] = (byte)'M';
    BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(2), size + 4);
    BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(10), 54);
    BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(14), 40);
    BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(18), w);
    BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(22), h);
    b[26] = 1; b[28] = 24;
    for (var y = 0; y < h; y++)
    for (var x = 0; x < w; x++)
    {
        var o = 54 + (h - 1 - y) * stride + x * 3;
        (b[o + 2], b[o + 1], b[o]) = px[x, y];
    }
    BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(size), seed);
    return b;
}

var green = Portrait(240, 300, (198, 228, 206), noise: true);
var amber = Portrait(240, 300, (246, 226, 180), noise: true);
var red = Portrait(240, 300, (240, 196, 196), noise: true);
var tiny = Portrait(60, 75, (210, 210, 230), noise: false);
var grey = Portrait(120, 150, (220, 220, 220), noise: false);

// Scores sit well inside their buckets (thresholds 30 and 60): >= 75 approves, 40-50 needs review, < 20 fails.
Make("approve.png", s => s >= 75, seed => Png(green, $"Scytales sample photo, seed {seed}"));
Make("manual-review.png", s => s is >= 40 and <= 50, seed => Png(amber, $"Scytales sample photo, seed {seed}"));
Make("fail-low-quality.png", s => s < 20, seed => Png(red, $"Scytales sample photo, seed {seed}"));
// The remaining samples score well above both thresholds, so that each fails for exactly one reason.
Make("fail-too-small.png", s => s >= 75, seed => Png(tiny, $"Scytales sample photo, seed {seed}"));
Make("fail-unsupported-type.bmp", s => s >= 75, seed => Bmp(grey, seed));

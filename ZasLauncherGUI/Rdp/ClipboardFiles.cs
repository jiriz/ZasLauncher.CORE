using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ZasLauncherGUI.Rdp;

internal record ClipboardFile(string Name, bool Directory, ulong Size, long WriteTime);
internal static class ClipboardFiles
{
    private static int _cleaned;
    private static string Cache => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZasLauncher", "clipboard");
    // Retain completed copies for later Finder paste; never remove the current pasteboard's paths.
    internal static void CleanupOld(IEnumerable<string> clipboardPaths)
    {
        if (Interlocked.Exchange(ref _cleaned, 1) != 0 || !System.IO.Directory.Exists(Cache)) return;
        var keep = clipboardPaths.Select(Path.GetFullPath).ToArray();
        foreach (var directory in System.IO.Directory.EnumerateDirectories(Cache))
        {
            if (!Guid.TryParseExact(Path.GetFileName(directory), "N", out _) ||
                System.IO.Directory.GetCreationTimeUtc(directory) > DateTime.UtcNow.AddDays(-7) ||
                keep.Any(p => p.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))) continue;
            try { System.IO.Directory.Delete(directory, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
    private const int DescriptorSize = 592, MaxFiles = 4096;
    internal static string ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length >= 260 || name.Contains('/') || name.Contains('\0'))
            throw new IOException("Neplatný nebo příliš dlouhý název souboru ve schránce.");
        var parts = name.Split('\\');
        if (parts.Any(p => p.Length == 0 || p is "." or ".." || p.EndsWith('.') || p.EndsWith(' ') || p.Any(c => c < 32 || "<>:\"|?*".Contains(c))))
            throw new IOException("Nepodporovaný název souboru ve schránce.");
        foreach (string part in parts)
        {
            string stem = part.Split('.')[0].ToUpperInvariant();
            if (stem is "CON" or "PRN" or "AUX" or "NUL" ||
                (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && stem[3] >= '1' && stem[3] <= '9'))
                throw new IOException("Název souboru je vyhrazený systémem Windows.");
        }
        return string.Join('\\', parts).Normalize(NormalizationForm.FormC);
    }
    public static (byte[] Descriptors, string[] Paths) Describe(IEnumerable<string> selected, CancellationToken cancellation = default)
    {
        var entries = new List<ClipboardFile>(); var paths = new List<string>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string path, string relative)
        {
            cancellation.ThrowIfCancellationRequested();
            relative = ValidateName(relative);
            if (!names.Add(relative)) throw new IOException("Soubory ve schránce mají shodné názvy.");
            if (entries.Count >= MaxFiles) throw new IOException("Schránka podporuje nejvýše 4096 položek najednou.");
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Odkazy na soubory nebo složky nelze přenášet přes RDP schránku.");
            bool directory = (attributes & FileAttributes.Directory) != 0;
            entries.Add(new ClipboardFile(relative, directory, directory ? 0 : checked((ulong)new FileInfo(path).Length),
                File.GetLastWriteTimeUtc(path).ToFileTimeUtc()));
            paths.Add(Path.GetFullPath(path));
            if (directory)
                foreach (var child in System.IO.Directory.EnumerateFileSystemEntries(path)) Add(child, relative + "\\" + Path.GetFileName(child));
        }
        foreach (var path in selected) Add(path, Path.GetFileName(Path.TrimEndingDirectorySeparator(path)));
        if (entries.Count == 0) throw new IOException("Ve schránce nejsou soubory.");
        var data = new byte[4 + entries.Count * DescriptorSize];
        BinaryPrimitives.WriteUInt32LittleEndian(data, (uint)entries.Count);
        for (int i=0;i<entries.Count;i++)
        {
            var entry = entries[i]; var d = data.AsSpan(4+i*DescriptorSize, DescriptorSize);
            BinaryPrimitives.WriteUInt32LittleEndian(d, 0x64); // attributes, write time, file size
            BinaryPrimitives.WriteUInt32LittleEndian(d[36..], entry.Directory ? 0x10u : 0x80u);
            BinaryPrimitives.WriteInt64LittleEndian(d[56..], entry.WriteTime);
            BinaryPrimitives.WriteUInt32LittleEndian(d[64..], (uint)(entry.Size>>32));
            BinaryPrimitives.WriteUInt32LittleEndian(d[68..], (uint)entry.Size);
            Encoding.Unicode.GetBytes(entry.Name.AsSpan(), d[72..]);
        }
        return (data, paths.ToArray());
    }
    public static IReadOnlyList<ClipboardFile> Parse(byte[] data)
    {
        if (data.Length < 4) throw new IOException("Neplatný seznam souborů v RDP schránce.");
        uint count = BinaryPrimitives.ReadUInt32LittleEndian(data);
        if (count == 0 || count > MaxFiles || data.Length != 4 + count*DescriptorSize) throw new IOException("Neplatná velikost seznamu souborů.");
        var files = new List<ClipboardFile>(); var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i=0;i<count;i++)
        {
            var d = data.AsSpan(4+i*DescriptorSize, DescriptorSize);
            uint flags=BinaryPrimitives.ReadUInt32LittleEndian(d), attributes=BinaryPrimitives.ReadUInt32LittleEndian(d[36..]);
            bool directory=(attributes&0x10)!=0;
            if ((flags&4)==0 || (!directory && (flags&0x40)==0) || (attributes&0x400)!=0)
                throw new IOException("RDP soubor nemá platné atributy nebo velikost.");
            var raw = Encoding.Unicode.GetString(d[72..]); int end=raw.IndexOf('\0');
            if (end<0) throw new IOException("Neplatný název RDP souboru.");
            string name=ValidateName(raw[..end]);
            if (!names.Add(name)) throw new IOException("Kolize názvů souborů ve vzdálené schránce.");
            ulong size=((ulong)BinaryPrimitives.ReadUInt32LittleEndian(d[64..])<<32)|BinaryPrimitives.ReadUInt32LittleEndian(d[68..]);
            files.Add(new ClipboardFile(name,directory,size,(flags&0x20)!=0?BinaryPrimitives.ReadInt64LittleEndian(d[56..]):0));
        }
        foreach (var entry in files)
            foreach (var other in files)
                if (!other.Directory && entry.Name.StartsWith(other.Name+"\\",StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Soubor koliduje s cestou složky ve schránce.");
        return files;
    }
    internal static void RemoveDownload(string topLevelPath)
    {
        try { System.IO.Directory.Delete(Path.GetDirectoryName(topLevelPath)!, true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
    public static async Task<string[]> DownloadAsync(IRdpClipboardConnection connection, byte[] descriptors, ulong generation,
        CancellationToken cancellation, Action<string> status)
    {
        var files=Parse(descriptors);
        var cache=Cache;
        System.IO.Directory.CreateDirectory(cache);
        var root=Path.Combine(cache,Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(root);
        try
        {
            ulong total=0;
            foreach (var file in files) if (!file.Directory) total=checked(total+file.Size);
            var disk=new DriveInfo(Path.GetPathRoot(root)!);
            if (total>(ulong)Math.Max(0,disk.AvailableFreeSpace-64*1024*1024)) throw new IOException("Pro soubory ze schránky není dost místa na disku.");
            var buffer=new byte[1024*1024];
            for (int i=0;i<files.Count;i++)
            {
                cancellation.ThrowIfCancellationRequested();
                var file=files[i]; var destination=Path.Combine(root,file.Name.Replace('\\',Path.DirectorySeparatorChar));
                if (file.Directory) {System.IO.Directory.CreateDirectory(destination);continue;}
                System.IO.Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                status($"Přenáším soubor {i+1}/{files.Count}: {file.Name}");
                await using (var stream=new FileStream(destination,FileMode.CreateNew,FileAccess.Write,FileShare.None,65536,true))
                {
                    ulong position=0;
                    while (position<file.Size)
                    {
                        int wanted=(int)Math.Min((ulong)buffer.Length,file.Size-position);
                        int bytes=await connection.ReadFileChunkAsync(generation,(uint)i,position,buffer,wanted,cancellation);
                        if (bytes<=0 || bytes>wanted) throw new IOException("Vzdálený soubor je neúplný.");
                        await stream.WriteAsync(buffer.AsMemory(0,bytes),cancellation);position+=(uint)bytes;
                    }
                }
                if (file.WriteTime>0) {
                    try {File.SetLastWriteTimeUtc(destination,DateTime.FromFileTimeUtc(file.WriteTime));}
                    catch (ArgumentOutOfRangeException) { }
                }
            }
            // Only expose complete top-level entries to Finder; never expose partial downloads.
            return files.Select(f=>f.Name.Split('\\')[0]).Distinct(StringComparer.OrdinalIgnoreCase).Select(n=>Path.Combine(root,n)).ToArray();
        }
        catch { try { System.IO.Directory.Delete(root,true); } catch (IOException) { } throw; }
    }
}

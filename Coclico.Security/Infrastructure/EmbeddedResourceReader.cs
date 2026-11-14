using System.IO;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Coclico.Security.Infrastructure;

public static class EmbeddedResourceReader
{
    public static byte[]? ReadResource(string peFilePath, string logicalName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(peFilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(logicalName);

        try
        {
            using FileStream stream = File.OpenRead(peFilePath);
            return ReadResource(stream, logicalName);
        }
        catch (Exception ex) when (ex is IOException or BadImageFormatException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static byte[]? ReadResource(Stream peStream, string logicalName)
    {
        ArgumentNullException.ThrowIfNull(peStream);
        ArgumentException.ThrowIfNullOrWhiteSpace(logicalName);

        try
        {
            using var peReader = new PEReader(peStream);
            if (!peReader.HasMetadata)
            {
                return null;
            }

            MetadataReader reader = peReader.GetMetadataReader();
            if (peReader.PEHeaders.CorHeader is not { } corHeader)
            {
                return null;
            }

            int resourcesRva = corHeader.ResourcesDirectory.RelativeVirtualAddress;
            if (resourcesRva == 0)
            {
                return null;
            }

            BlobReader blob = peReader.GetSectionData(resourcesRva).GetReader();

            foreach (ManifestResourceHandle handle in reader.ManifestResources)
            {
                ManifestResource resource = reader.GetManifestResource(handle);
                if (!resource.Implementation.IsNil)
                {
                    continue;
                }

                if (!string.Equals(reader.GetString(resource.Name), logicalName, StringComparison.Ordinal))
                {
                    continue;
                }

                long offset = resource.Offset;
                if (offset < 0 || offset + 4 > blob.Length)
                {
                    return null;
                }

                blob.Offset = (int)offset;
                int length = blob.ReadInt32();
                if (length < 0 || blob.Offset + length > blob.Length)
                {
                    return null;
                }

                return blob.ReadBytes(length);
            }

            return null;
        }
        catch (Exception ex) when (ex is BadImageFormatException or IOException)
        {
            return null;
        }
    }
}

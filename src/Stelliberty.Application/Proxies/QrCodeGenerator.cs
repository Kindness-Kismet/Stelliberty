using Stelliberty.Domain.Proxies;
using QRCoder;

namespace Stelliberty.Application.Proxies;

public sealed class QrCodeGenerator
{
    public QrCodeMatrix Generate(string content)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(content, QRCodeGenerator.ECCLevel.Q);
        var size = data.ModuleMatrix.Count;
        var modules = new bool[size * size];
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                modules[y * size + x] = data.ModuleMatrix[y][x];
            }
        }

        return new QrCodeMatrix(size, modules);
    }
}

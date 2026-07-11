using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Stelliberty.Application.Proxies;
using Stelliberty.Domain.Proxies;

namespace Stelliberty.Desktop.Controls;

public sealed class QrCodeMatrixView : Control
{
    public static readonly StyledProperty<QrCodeMatrix?> MatrixProperty =
        AvaloniaProperty.Register<QrCodeMatrixView, QrCodeMatrix?>(nameof(Matrix));

    static QrCodeMatrixView()
    {
        AffectsRender<QrCodeMatrixView>(MatrixProperty);
    }

    public QrCodeMatrix? Matrix
    {
        get => GetValue(MatrixProperty);
        set => SetValue(MatrixProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Matrix is null || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        var side = Math.Min(Bounds.Width, Bounds.Height);
        var cell = side / Matrix.Size;
        var left = (Bounds.Width - side) / 2;
        var top = (Bounds.Height - side) / 2;
        context.FillRectangle(Brushes.White, new Rect(left, top, side, side));

        for (var y = 0; y < Matrix.Size; y++)
        {
            for (var x = 0; x < Matrix.Size; x++)
            {
                if (!Matrix.IsDark(x, y))
                {
                    continue;
                }

                context.FillRectangle(Brushes.Black, new Rect(left + x * cell, top + y * cell, cell, cell));
            }
        }
    }
}

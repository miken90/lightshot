// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using Lightshot.App.Views.QuickAccess;
using Lightshot.Core;
using Lightshot.Platform.Windows.Interop;
using Lightshot.TestSupport;
using SkiaSharp;
using Xunit;
using Size = Lightshot.Core.Size;

namespace Lightshot.App.UiTests;

public class QuickAccessFlowTests
{
    private static void RunInSta(Action action)
    {
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
        {
            action();
            return;
        }

        ExceptionDispatchInfo? captured = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                captured = ExceptionDispatchInfo.Capture(ex);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        captured?.Throw();
    }

    private static CapturedImage CreateTestImage(int width = 240, int height = 160)
    {
        using var bmp = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.DarkSeaGreen);
        using var data = bmp.Encode(SKEncodedImageFormat.Png, 100);
        return new CapturedImage(width, height, data.ToArray());
    }

    [Fact]
    [Desktop]
    public void DragOutClosesCardUnlessAltHeld()
    {
        RunInSta(() =>
        {
            var image = CreateTestImage(240, 160);
            var id = Guid.NewGuid();
            var cardVm = new CardViewModel(id, image);

            // Case 1: Default setting (CloseAfterDragging = true)
            // Drag-out without Alt -> closes card
            var card1 = new CardWindow(Guid.NewGuid(), image, cardVm);
            bool closedWithoutAlt = false;
            card1.RequestClose = _ => closedWithoutAlt = true;

            card1.Show();
            Win32Window.PumpMessages(5);

            try
            {
                // Simulate completed drag-out without Alt held
                card1.HandleDragDropCompleted(DragDropEffects.Copy, altHeld: false);
                Assert.True(closedWithoutAlt, "Card should request close when dragged out without Alt held.");

                // Case 2: Drag-out with Alt held -> card stays open
                var card2 = new CardWindow(Guid.NewGuid(), image, cardVm);
                bool closedWithAlt = false;
                card2.RequestClose = _ => closedWithAlt = true;

                card2.HandleDragDropCompleted(DragDropEffects.Copy, altHeld: true);
                Assert.False(closedWithAlt, "Card should NOT request close when Alt was held during drag-out.");

                // Case 3: Inverted setting (CloseAfterDragging = false)
                // Alt held inverts the setting
                var settingsInverted = new QuickAccessSettings { CloseAfterDragging = false };
                var card3 = new CardWindow(Guid.NewGuid(), image, cardVm, () => settingsInverted);
                bool closedInvertedNoAlt = false;
                bool closedInvertedWithAlt = false;

                card3.RequestClose = _ => closedInvertedNoAlt = true;
                card3.HandleDragDropCompleted(DragDropEffects.Copy, altHeld: false);
                Assert.False(closedInvertedNoAlt, "Card with CloseAfterDragging=false should stay open when Alt is not held.");

                card3.RequestClose = _ => closedInvertedWithAlt = true;
                card3.HandleDragDropCompleted(DragDropEffects.Copy, altHeld: true);
                Assert.True(closedInvertedWithAlt, "Card with CloseAfterDragging=false should close when Alt is held.");

                // Case 4: Drag cancelled (effect == None) -> card never closes
                var card4 = new CardWindow(Guid.NewGuid(), image, cardVm);
                bool closedCancelled = false;
                card4.RequestClose = _ => closedCancelled = true;

                card4.HandleDragDropCompleted(DragDropEffects.None, altHeld: false);
                Assert.False(closedCancelled, "Card should NOT close when drag was cancelled.");
            }
            finally
            {
                card1.Close();
                Win32Window.PumpMessages(5);
            }
        });
    }
}

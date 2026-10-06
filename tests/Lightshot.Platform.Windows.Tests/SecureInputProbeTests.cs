// Ported from LightshotKit/Tests/LightshotKitTests/KeystrokeOverlayTests.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Lightshot.Platform.Windows.Input;
using Lightshot.Platform.Windows.Interop;
using Lightshot.Platform.Windows.Overlay;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.Platform.Windows.Tests;

public class SecureInputProbeTests
{
    [Fact]
    [Desktop]
    public void PasswordFieldRaisesSecureInput()
    {
        Exception? threadException = null;
        var thread = new Thread(() =>
        {
            try
            {
                RunPasswordFieldTest();
            }
            catch (Exception ex)
            {
                threadException = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        bool finished = thread.Join(20000);

        Assert.True(finished, "WPF STA test thread timed out.");
        if (threadException != null)
        {
            throw new Exception("WPF STA test thread failed", threadException);
        }
    }

    private static void RunPasswordFieldTest()
    {
        using var probe = new SecureInputProbe();
        probe.Start();

        var window = new Window
        {
            Title = "SecureInputProbeTest",
            Width = 300,
            Height = 200,
            WindowStyle = WindowStyle.ToolWindow,
            ShowInTaskbar = false,
            Topmost = true
        };

        var panel = new StackPanel();
        var regularBox = new TextBox { Width = 200, Height = 30 };
        var passwordBox = new PasswordBox { Width = 200, Height = 30 };
        panel.Children.Add(regularBox);
        panel.Children.Add(passwordBox);
        window.Content = panel;

        window.Show();
        window.Activate();
        PumpMessages(100);

        try
        {
            // 1. Initial state: focus regular text box -> secure input is false
            regularBox.Focus();
            Keyboard.Focus(regularBox);
            PumpMessages(200);
            Assert.False(probe.IsSecureInput, "Regular text box must not trigger secure input.");

            // 2. Focus password box -> probe detects password field and raises secure input
            passwordBox.Focus();
            Keyboard.Focus(passwordBox);
            bool becameSecure = WaitForCondition(() => probe.IsSecureInput, TimeSpan.FromSeconds(4));
            Assert.True(becameSecure, "Focusing PasswordBox must raise secure input (IsSecureInput == true).");

            // 3. Move focus back to regular text box -> secure input clears
            regularBox.Focus();
            Keyboard.Focus(regularBox);
            bool clearedSecure = WaitForCondition(() => !probe.IsSecureInput, TimeSpan.FromSeconds(4));
            Assert.True(clearedSecure, "Focusing regular TextBox must clear secure input (IsSecureInput == false).");
        }
        finally
        {
            probe.Stop();
            window.Close();
            PumpMessages(50);
        }
    }

    private static bool WaitForCondition(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return true;
            PumpMessages(50);
        }
        return condition();
    }

    private static void PumpMessages(int durationMs)
    {
        var end = DateTime.UtcNow.AddMilliseconds(durationMs);
        do
        {
            Win32Window.PumpMessages(5);
            Thread.Sleep(10);
        } while (DateTime.UtcNow < end);
    }
}

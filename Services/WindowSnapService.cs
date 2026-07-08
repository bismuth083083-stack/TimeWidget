using System.Windows;

namespace TimeWidget.Services;

public static class WindowSnapService
{
    private const double SnapDistance = 16;
    private const double WidgetGap = 16;

    public static void SnapToScreen(Window window)
    {
        SnapToOtherWidgets(window);

        double leftEdge = SystemParameters.VirtualScreenLeft;
        double topEdge = SystemParameters.VirtualScreenTop;
        double rightEdge = SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth;
        double bottomEdge = SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight;
        double leftSnapPosition = leftEdge + WidgetGap;
        double topSnapPosition = topEdge + WidgetGap;
        double rightSnapPosition = rightEdge - WidgetGap;
        double bottomSnapPosition = bottomEdge - WidgetGap;

        double width = GetWindowWidth(window);
        double height = GetWindowHeight(window);
        double windowRight = window.Left + width;
        double windowBottom = window.Top + height;
        double centeredLeft = leftEdge + (SystemParameters.VirtualScreenWidth - width) / 2;
        double centeredTop = topEdge + (SystemParameters.VirtualScreenHeight - height) / 2;

        if (IsNear(window.Left, leftSnapPosition))
        {
            window.Left = leftSnapPosition;
        }
        else if (IsNear(windowRight, rightSnapPosition))
        {
            window.Left = rightSnapPosition - width;
        }
        else if (IsNear(window.Left, centeredLeft))
        {
            window.Left = centeredLeft;
        }

        if (IsNear(window.Top, topSnapPosition))
        {
            window.Top = topSnapPosition;
        }
        else if (IsNear(windowBottom, bottomSnapPosition))
        {
            window.Top = bottomSnapPosition - height;
        }
        else if (IsNear(window.Top, centeredTop))
        {
            window.Top = centeredTop;
        }

        KeepWindowOnScreen(window);
    }

    public static void KeepWindowOnScreen(Window window)
    {
        double minLeft = SystemParameters.VirtualScreenLeft;
        double minTop = SystemParameters.VirtualScreenTop;
        double maxLeft = SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - GetWindowWidth(window);
        double maxTop = SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - GetWindowHeight(window);

        window.Left = Math.Clamp(window.Left, minLeft, maxLeft);
        window.Top = Math.Clamp(window.Top, minTop, maxTop);
    }

    private static void SnapToOtherWidgets(Window movingWindow)
    {
        double movingWidth = GetWindowWidth(movingWindow);
        double movingHeight = GetWindowHeight(movingWindow);
        double movingRight = movingWindow.Left + movingWidth;
        double movingBottom = movingWindow.Top + movingHeight;

        SnapCandidate? horizontalCandidate = null;
        SnapCandidate? verticalCandidate = null;

        foreach (Window otherWindow in Application.Current.Windows)
        {
            if (ReferenceEquals(movingWindow, otherWindow)
                || !otherWindow.IsVisible
                || !IsWidgetWindow(otherWindow))
            {
                continue;
            }

            double otherWidth = GetWindowWidth(otherWindow);
            double otherHeight = GetWindowHeight(otherWindow);
            double otherRight = otherWindow.Left + otherWidth;
            double otherBottom = otherWindow.Top + otherHeight;

            if (RangesTouchOrOverlap(movingWindow.Top, movingBottom, otherWindow.Top, otherBottom))
            {
                TrySetCloserCandidate(ref horizontalCandidate, movingWindow.Left, otherRight + WidgetGap);
                TrySetCloserCandidate(ref horizontalCandidate, movingRight, otherWindow.Left - WidgetGap, otherWindow.Left - WidgetGap - movingWidth);
            }

            if (RangesTouchOrOverlap(movingWindow.Left, movingRight, otherWindow.Left, otherRight))
            {
                TrySetCloserCandidate(ref verticalCandidate, movingWindow.Top, otherBottom + WidgetGap);
                TrySetCloserCandidate(ref verticalCandidate, movingBottom, otherWindow.Top - WidgetGap, otherWindow.Top - WidgetGap - movingHeight);
            }
        }

        if (horizontalCandidate.HasValue)
        {
            movingWindow.Left = horizontalCandidate.Value.TargetPosition;
        }

        if (verticalCandidate.HasValue)
        {
            movingWindow.Top = verticalCandidate.Value.TargetPosition;
        }
    }

    private static bool IsWidgetWindow(Window window)
    {
        return window is MainWindow or FolderWidgetWindow or MediaWidgetWindow or WeatherWidgetWindow;
    }

    private static bool RangesTouchOrOverlap(double firstStart, double firstEnd, double secondStart, double secondEnd)
    {
        return firstStart <= secondEnd + SnapDistance && firstEnd >= secondStart - SnapDistance;
    }

    private static void TrySetCloserCandidate(ref SnapCandidate? candidate, double currentValue, double targetValue)
    {
        TrySetCloserCandidate(ref candidate, currentValue, targetValue, targetValue);
    }

    private static void TrySetCloserCandidate(
        ref SnapCandidate? candidate,
        double currentValue,
        double targetValue,
        double targetPosition)
    {
        double distance = Math.Abs(currentValue - targetValue);
        if (distance > SnapDistance)
        {
            return;
        }

        if (!candidate.HasValue || distance < candidate.Value.Distance)
        {
            candidate = new SnapCandidate(targetPosition, distance);
        }
    }

    private static double GetWindowWidth(Window window)
    {
        return window.ActualWidth > 0 ? window.ActualWidth : window.Width;
    }

    private static double GetWindowHeight(Window window)
    {
        return window.ActualHeight > 0 ? window.ActualHeight : window.Height;
    }

    private static bool IsNear(double value, double target)
    {
        return Math.Abs(value - target) <= SnapDistance;
    }

    private readonly record struct SnapCandidate(double TargetPosition, double Distance);
}

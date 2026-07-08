using System.Windows;

namespace TimeWidget.Services;

public static class WindowSnapService
{
    private const double SnapDistance = 16;
    private const double WidgetGap = 16;
    private const double ShadowPadding = 8;

    public static void SnapToScreen(Window window)
    {
        SnapToOtherWidgets(window);

        double leftEdge = SystemParameters.VirtualScreenLeft;
        double topEdge = SystemParameters.VirtualScreenTop;
        double rightEdge = SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth;
        double bottomEdge = SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight;
        double leftSnapPosition = leftEdge + WidgetGap - ShadowPadding;
        double topSnapPosition = topEdge + WidgetGap - ShadowPadding;
        double rightSnapPosition = rightEdge - WidgetGap + ShadowPadding;
        double bottomSnapPosition = bottomEdge - WidgetGap + ShadowPadding;

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
        double minLeft = SystemParameters.VirtualScreenLeft + WidgetGap - ShadowPadding;
        double minTop = SystemParameters.VirtualScreenTop + WidgetGap - ShadowPadding;
        double maxLeft = SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - GetWindowWidth(window) - WidgetGap + ShadowPadding;
        double maxTop = SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - GetWindowHeight(window) - WidgetGap + ShadowPadding;

        window.Left = Math.Clamp(window.Left, minLeft, maxLeft);
        window.Top = Math.Clamp(window.Top, minTop, maxTop);
    }

    public static void SnapResize(Window window)
    {
        double width = GetWindowWidth(window);
        double height = GetWindowHeight(window);
        double right = window.Left + width;
        double bottom = window.Top + height;
        double targetWidth = width;
        double targetHeight = height;
        bool verticalGuide = false;
        bool horizontalGuide = false;

        double screenRightTarget = SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - WidgetGap + ShadowPadding;
        double screenBottomTarget = SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - WidgetGap + ShadowPadding;

        if (IsNear(right, screenRightTarget))
        {
            targetWidth = Math.Max(window.MinWidth, screenRightTarget - window.Left);
            verticalGuide = true;
        }

        if (IsNear(bottom, screenBottomTarget))
        {
            targetHeight = Math.Max(window.MinHeight, screenBottomTarget - window.Top);
            horizontalGuide = true;
        }

        foreach (Window otherWindow in Application.Current.Windows)
        {
            if (ReferenceEquals(window, otherWindow)
                || !otherWindow.IsVisible
                || !IsWidgetWindow(otherWindow))
            {
                continue;
            }

            double otherRight = otherWindow.Left + GetWindowWidth(otherWindow);
            double otherBottom = otherWindow.Top + GetWindowHeight(otherWindow);

            if (RangesTouchOrOverlap(window.Top, bottom, otherWindow.Top, otherBottom))
            {
                if (TryGetResizeTarget(right, otherWindow.Left, window.Left, window.MinWidth, ref targetWidth)
                    || TryGetResizeTarget(right, otherRight, window.Left, window.MinWidth, ref targetWidth))
                {
                    verticalGuide = true;
                }
            }

            if (RangesTouchOrOverlap(window.Left, right, otherWindow.Left, otherRight))
            {
                if (TryGetResizeTarget(bottom, otherWindow.Top, window.Top, window.MinHeight, ref targetHeight)
                    || TryGetResizeTarget(bottom, otherBottom, window.Top, window.MinHeight, ref targetHeight))
                {
                    horizontalGuide = true;
                }
            }
        }

        if (Math.Abs(targetWidth - width) > 0.5)
        {
            window.Width = targetWidth;
        }

        if (Math.Abs(targetHeight - height) > 0.5)
        {
            window.Height = targetHeight;
        }

        KeepWindowOnScreen(window);

        if (verticalGuide)
        {
            SnapGuideService.ShowVertical(window.Left + GetWindowWidth(window) - ShadowPadding);
        }

        if (horizontalGuide)
        {
            SnapGuideService.ShowHorizontal(window.Top + GetWindowHeight(window) - ShadowPadding);
        }
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
                TrySetCloserCandidate(ref horizontalCandidate, movingWindow.Left, otherRight + WidgetGap - (ShadowPadding * 2));
                TrySetCloserCandidate(ref horizontalCandidate, movingRight, otherWindow.Left - WidgetGap + (ShadowPadding * 2), otherWindow.Left - WidgetGap + (ShadowPadding * 2) - movingWidth);
            }

            if (RangesTouchOrOverlap(movingWindow.Left, movingRight, otherWindow.Left, otherRight))
            {
                TrySetCloserCandidate(ref verticalCandidate, movingWindow.Top, otherBottom + WidgetGap - (ShadowPadding * 2));
                TrySetCloserCandidate(ref verticalCandidate, movingBottom, otherWindow.Top - WidgetGap + (ShadowPadding * 2), otherWindow.Top - WidgetGap + (ShadowPadding * 2) - movingHeight);
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
        return window is MainWindow
            or FolderWidgetWindow
            or MediaWidgetWindow
            or WeatherWidgetWindow
            or AlarmWidgetWindow
            or TimerWidgetWindow
            or AiSearchWidgetWindow
            or CalendarWidgetWindow;
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

    private static bool TryGetResizeTarget(
        double currentEdge,
        double targetEdge,
        double fixedEdge,
        double minSize,
        ref double targetSize)
    {
        if (!IsNear(currentEdge, targetEdge))
        {
            return false;
        }

        targetSize = Math.Max(minSize, targetEdge - fixedEdge);
        return true;
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

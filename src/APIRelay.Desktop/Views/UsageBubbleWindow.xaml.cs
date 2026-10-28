using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using APIRelay.Desktop.ViewModels;

namespace APIRelay.Desktop.Views;

public partial class UsageBubbleWindow : Window
{
    private readonly RelayEngine engine;
    private Storyboard? toastAnimation;
    internal UsageBubbleWindow(RelayEngine engine)
    {
        this.engine = engine;
        InitializeComponent();
        Left = Math.Clamp(engine.BubbleX ?? SystemParameters.WorkArea.Right - Width - 24,
            SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - Width);
        Top = Math.Clamp(engine.BubbleY ?? SystemParameters.WorkArea.Bottom - Height - 24,
            SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - Height);
        IsVisibleChanged += (_, _) => { if (!IsVisible) HideToast(); };
        LocationChanged += (_, _) => HideToast();
        Closed += (_, _) => HideToast();
    }
    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) { App.CurrentApp.ShowWorkspace(); return; }
        DragMove();
        SaveLocation();
    }
    internal void SaveLocation()
    {
        engine.BubbleX = (int)Math.Round(Left);
        engine.BubbleY = (int)Math.Round(Top);
    }
    internal void ShowToast()
    {
        if (!IsVisible) return;
        HideToast();
        CostToastPopup.IsOpen = true;

        var rise = new DoubleAnimation(0, -26, TimeSpan.FromSeconds(1.8))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(rise, CostToast);
        Storyboard.SetTargetProperty(rise, new PropertyPath("(UIElement.RenderTransform).(TranslateTransform.Y)"));

        var fade = new DoubleAnimationUsingKeyFrames();
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.12))));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.9))));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.8))));
        Storyboard.SetTarget(fade, CostToast);
        Storyboard.SetTargetProperty(fade, new PropertyPath(OpacityProperty));

        var animation = new Storyboard();
        animation.Children.Add(rise);
        animation.Children.Add(fade);
        animation.Completed += (_, _) =>
        {
            if (ReferenceEquals(toastAnimation, animation)) HideToast();
        };
        toastAnimation = animation;
        animation.Begin(this, HandoffBehavior.SnapshotAndReplace, isControllable: true);
    }
    private void HideToast()
    {
        toastAnimation?.Remove(this);
        toastAnimation = null;
        CostToastPopup.IsOpen = false;
    }
    private void Open_Click(object sender, RoutedEventArgs e) => App.CurrentApp.ShowWorkspace();
    private void Hide_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is ShellViewModel model) model.ShowBubble = false;
    }
    private void Quit_Click(object sender, RoutedEventArgs e) => _ = App.CurrentApp.QuitAsync();
}

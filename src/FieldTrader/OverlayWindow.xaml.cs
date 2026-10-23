using System.Windows;
using System.Windows.Input;
namespace FieldTrader;
public partial class OverlayWindow : Window {
 private bool locked,compact;
 public OverlayWindow(SessionState session) { InitializeComponent(); DataContext=session; Left=40; Top=70; }
 private void ScrollCandidates(object sender,System.Windows.Input.MouseWheelEventArgs e){if(sender is System.Windows.Controls.ScrollViewer scroll&&scroll.ScrollableWidth>0){scroll.ScrollToHorizontalOffset(scroll.HorizontalOffset-e.Delta);e.Handled=true;}}
 private void DragHeader(object sender,MouseButtonEventArgs e) { if(!locked&&e.ChangedButton==MouseButton.Left) { try { DragMove(); } catch(InvalidOperationException) { } } }
 private void ToggleLock(object sender,RoutedEventArgs e) { locked=!locked; LockButton.Content=locked?"解锁":"锁定"; }
 private void ToggleCompact(object sender,RoutedEventArgs e) { compact=!compact; ResultLabel.Visibility=Visibility.Visible; OpacityPanel.Visibility=compact?Visibility.Collapsed:Visibility.Visible; Cards.ItemTemplate=(DataTemplate)FindResource(compact?"CompactCandidateTemplate":"HudCandidateTemplate"); Cards.Height=compact?96:204; PreviousCards.ItemTemplate=Cards.ItemTemplate; PreviousCards.Height=Cards.Height; Width=compact?370:480; }
 private void ToggleOptions(object sender,RoutedEventArgs e)=>OpacityPanel.Visibility=OpacityPanel.Visibility==Visibility.Visible?Visibility.Collapsed:Visibility.Visible;
 private void HideOverlay(object sender,RoutedEventArgs e)=>Close();
}



using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
namespace FieldTrader;
public partial class OverlayWindow : Window {
 private bool locked,compact;
 private readonly SessionState session;
 public OverlayWindow(SessionState session) {
  InitializeComponent();
  this.session=session;
  DataContext=session;
  Left=40;
  Top=70;
  var settings=UserSettingsStore.Load(session.Store.Root);
  Width=settings.OverlayWidth;
  if(settings.OverlayHeight>0){SizeToContent=SizeToContent.Manual;Height=settings.OverlayHeight;}
 }
 private void ScrollCandidates(object sender,System.Windows.Input.MouseWheelEventArgs e){if(sender is System.Windows.Controls.ScrollViewer scroll&&scroll.ScrollableWidth>0){scroll.ScrollToHorizontalOffset(scroll.HorizontalOffset-e.Delta);e.Handled=true;}}
 private void DragHeader(object sender,MouseButtonEventArgs e) { if(!locked&&e.ChangedButton==MouseButton.Left) { try { DragMove(); } catch(InvalidOperationException) { } } }
 private void ToggleLock(object sender,RoutedEventArgs e) { locked=!locked; LockButton.Content=locked?"解锁":"锁定"; }
 private void ToggleCompact(object sender,RoutedEventArgs e) { compact=!compact; ResultLabel.Visibility=Visibility.Visible; OpacityPanel.Visibility=compact?Visibility.Collapsed:Visibility.Visible; Cards.ItemTemplate=(DataTemplate)FindResource(compact?"CompactCandidateTemplate":"HudCandidateTemplate"); Cards.Height=compact?96:204; PreviousCards.ItemTemplate=Cards.ItemTemplate; PreviousCards.Height=Cards.Height; }
 private void ToggleOptions(object sender,RoutedEventArgs e)=>OpacityPanel.Visibility=OpacityPanel.Visibility==Visibility.Visible?Visibility.Collapsed:Visibility.Visible;
 private void HideOverlay(object sender,RoutedEventArgs e)=>Close();
 private void ResizeOverlay(object sender,DragDeltaEventArgs e) {
  if(locked||sender is not Thumb thumb)return;
  ResizeBy(thumb.Tag?.ToString()??"",e.HorizontalChange,e.VerticalChange);
 }
 internal void ResizeBy(string tag,double horizontalChange,double verticalChange) {
  if(SizeToContent!=SizeToContent.Manual){Width=ActualWidth;Height=ActualHeight;SizeToContent=SizeToContent.Manual;}
  double width=Width,height=Height,left=Left,top=Top;
  if(tag.Contains("Left",StringComparison.Ordinal)){width=Math.Clamp(Width-horizontalChange,MinWidth,MaxWidth);left+=Width-width;}
  if(tag.Contains("Right",StringComparison.Ordinal))width=Math.Clamp(Width+horizontalChange,MinWidth,MaxWidth);
  if(tag.Contains("Top",StringComparison.Ordinal)){height=Math.Clamp(Height-verticalChange,MinHeight,MaxHeight);top+=Height-height;}
  if(tag.Contains("Bottom",StringComparison.Ordinal))height=Math.Clamp(Height+verticalChange,MinHeight,MaxHeight);
  Width=width;Height=height;Left=left;Top=top;
 }
 private void ResizeCompleted(object sender,DragCompletedEventArgs e) {
  if(locked||SizeToContent!=SizeToContent.Manual)return;
  SaveSize();
 }
 internal void SaveSize() {
  UserSettingsStore.Update(session.Store.Root,s=>s with{OverlayWidth=Math.Round(Width),OverlayHeight=Math.Round(Height)});
 }
}



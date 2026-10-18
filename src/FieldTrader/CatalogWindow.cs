using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
namespace FieldTrader;
public sealed class CatalogWindow : Window {
 private readonly SessionState session; private readonly ListBox items=new(){Height=200,DisplayMemberPath="Name",Background=new SolidColorBrush(Color.FromRgb(20,31,43)),BorderThickness=new Thickness(0)}; private readonly ComboBox groups=new(); private readonly TextBox name=new(); private readonly TextBox groupName=new(); private readonly TextBlock info=new(){TextWrapping=TextWrapping.Wrap}; private string? imagePath;
 public CatalogWindow(SessionState session) {
  this.session=session; Style=(Style)FindResource(typeof(Window)); Title="物品与声音库"; Width=650; Height=740; WindowStartupLocation=WindowStartupLocation.CenterOwner;
  var panel=new StackPanel{Margin=new Thickness(24)}; var canvas=new Grid();canvas.Children.Add(new HachimiBackdrop{IsHitTestVisible=false});canvas.Children.Add(new ScrollViewer{Content=panel});Content=canvas;
  panel.Children.Add(new TextBlock{Text="物品与声音库",FontSize=24}); panel.Children.Add(new TextBlock{Text="同组可包含多个物品。样本请从拿起声音开始，避免前置点击声；只比对首段。",Margin=new Thickness(0,12,0,16)}); panel.Children.Add(items);
  items.SelectionChanged+=(_,_)=> { if(items.SelectedItem is Item i) { name.Text=i.Name; groups.SelectedItem=session.Store.Data.SoundGroups.FirstOrDefault(g=>g.Id==i.SoundGroupId); imagePath=i.ImagePath; } };
  panel.Children.Add(new TextBlock{Text="物品名称（选择已有物品可编辑）",Margin=new Thickness(0,14,0,0)}); panel.Children.Add(name); panel.Children.Add(new TextBlock{Text="所属声音组"}); panel.Children.Add(groups);
  var row=new WrapPanel{Margin=new Thickness(0,14,0,14)}; panel.Children.Add(row); AddButton(row,"选择图片",()=> { var dialog=new OpenFileDialog{Filter="图片|*.png;*.jpg;*.jpeg;*.bmp"}; if(dialog.ShowDialog()==true) { imagePath=session.Store.Import(dialog.FileName,"images"); info.Text="已导入图片"; } });
  AddButton(row,"保存物品",()=> { if(string.IsNullOrWhiteSpace(name.Text)||groups.SelectedItem is not SoundGroup g) throw new InvalidOperationException("请填写名称并选择声音组"); var old=items.SelectedItem as Item; var item=old is null?new Item(Guid.NewGuid().ToString(),name.Text.Trim(),imagePath,g.Id):old with{Name=name.Text.Trim(),ImagePath=imagePath,SoundGroupId=g.Id}; if(old!=null)session.Store.Data.Items.Remove(old); session.Store.Data.Items.Add(item); Save(); });
  AddButton(row,"新建物品",()=>{items.SelectedItem=null; name.Clear(); imagePath=null;});
  AddButton(row,"删除所选",()=> { if(items.SelectedItem is Item i){session.Store.Data.Items.Remove(i); Save();} });
  panel.Children.Add(new TextBlock{Text="新增声音组"}); panel.Children.Add(groupName); var groupRow=new WrapPanel(); panel.Children.Add(groupRow); AddButton(groupRow,"添加声音组",()=>{if(string.IsNullOrWhiteSpace(groupName.Text))return; session.Store.Data.SoundGroups.Add(new(Guid.NewGuid().ToString(),groupName.Text.Trim())); Save(); groupName.Clear();}); AddButton(groupRow,"导入声音样本到所选组",()=> { if(groups.SelectedItem is not SoundGroup g)throw new InvalidOperationException("请先选择声音组"); var dialog=new OpenFileDialog{Filter="声音样本|*.wav;*.mp3;*.flac;*.aiff"}; if(dialog.ShowDialog()==true) { session.Store.Data.SoundSamples.Add(new(Guid.NewGuid().ToString(),g.Id,session.Store.Import(dialog.FileName,"samples"),System.IO.Path.GetFileName(dialog.FileName))); Save(); } });
  info.Margin=new Thickness(0,20,0,0); panel.Children.Add(info); Refresh();
 }
 private void AddButton(Panel panel,string text,Action action){var b=new Button{Content=text,Margin=new Thickness(0,0,8,8)}; b.Click+=(_,_)=>{try{action();}catch(Exception ex){info.Text="操作失败："+ex.Message;}};panel.Children.Add(b);}
 private void Refresh(){var id=(groups.SelectedItem as SoundGroup)?.Id;items.ItemsSource=session.Store.Data.Items.ToArray();groups.ItemsSource=session.Store.Data.SoundGroups.ToArray();groups.SelectedItem=session.Store.Data.SoundGroups.FirstOrDefault(g=>g.Id==id)??session.Store.Data.SoundGroups.FirstOrDefault();info.Text=session.CatalogSummary+"\n数据目录："+session.Store.Root;}
 private void Save(){session.Store.Save();session.CatalogChanged();Refresh();}
}




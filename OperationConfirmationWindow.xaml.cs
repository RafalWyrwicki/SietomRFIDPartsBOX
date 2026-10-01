using System.Windows;

namespace PartsBox;
public partial class OperationConfirmationWindow : Window
{
 public OperationConfirmationWindow(string message)
 {
  InitializeComponent();Title="Informacja o zleceniu";Heading.Text="Zlecenie nieaktywne";Quantity.Visibility=Visibility.Collapsed;
  ((System.Windows.Controls.Panel)Quantity.Parent).Visibility=Visibility.Collapsed;
  Details.Text=message;Done.Content="Wróć do zleceń";
  // The regular success subtitle is not appropriate for a blocked order.
  var panel=(System.Windows.Controls.Panel)Heading.Parent;
  foreach(var child in panel.Children.OfType<System.Windows.Controls.TextBlock>())if(child.Text=="Operacja została zapisana pomyślnie.")child.Visibility=Visibility.Collapsed;
  if(panel.Children[0] is UIElement symbol)symbol.Visibility=Visibility.Collapsed;
 }
 public OperationConfirmationWindow(bool returned,int quantity)
 {
  InitializeComponent();
  Title=returned?"Potwierdzenie zwrotu":"Potwierdzenie pobrania";
  Heading.Text=returned?"Zwrot zatwierdzony":"Pobranie zatwierdzone";
  Quantity.Text=quantity.ToString("N0");
  Details.Text=returned?"Części oczekują na rozliczenie magazynowe.\nPo zamknięciu komunikatu nastąpi wylogowanie.":"Części zostały pobrane na wybrane zlecenie.\nPo zamknięciu komunikatu nastąpi wylogowanie.";
  Loaded+=(_,_)=>Done.Focus();
 }
 void Done_Click(object sender,RoutedEventArgs e)=>Close();
}

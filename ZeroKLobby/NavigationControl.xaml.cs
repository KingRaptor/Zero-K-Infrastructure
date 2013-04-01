﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms.Integration;
using ZeroKLobby.Controls;
using ZeroKLobby.MicroLobby;
using System.Collections.ObjectModel;

namespace ZeroKLobby
{
  /// <summary>
  /// Interaction logic for NavigationControl.xaml
  /// </summary>
  public partial class NavigationControl: UserControl, INotifyPropertyChanged
  {
    bool CanGoBack { get { return backStack.Any(); } }

    bool CanGoForward { get { return forwardStack.Any(); } }
    INavigatable CurrentINavigatable { get { return GetINavigatableFromControl(tabControl.SelectedContent); } }

		public bool BusyLoading {
			set { 
				busyIndicator.Visibility = value ? Visibility.Visible : Visibility.Hidden;
			} }

    NavigationStep CurrentPage
    {
      get { return _currentPage; }
      set
      {
        _currentPage = value;
        PropertyChanged(this, new PropertyChangedEventArgs("CurrentPage"));
        PropertyChanged(this, new PropertyChangedEventArgs("Path"));
        foreach (var b in Buttons)
        {
          b.IsSelected = Path.StartsWith(b.TargetPath);
          if (b.IsSelected) b.IsAlerting = false;
        }

        var steps = Path.Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries); // todo cleanup
        var navigable = tabControl.Items.OfType<Object>().Select(GetINavigatableFromControl).FirstOrDefault(x => x != null && x.PathHead == steps[0]);
        if (navigable != null) navigable.Hilite(HiliteLevel.None, steps);
      }
    }

    NavigationStep _currentPage;
    readonly Stack<NavigationStep> backStack = new Stack<NavigationStep>();
    readonly Stack<NavigationStep> forwardStack = new Stack<NavigationStep>();
    readonly List<string> lastPaths = new List<string>();
    public WebBrowser Browser { get { return browserControl.WebBrowser; } }
    public ObservableCollection<ButtonInfo> Buttons { get; set; }
    public ChatTab ChatTab { get { return chatTab; } }
    public static NavigationControl Instance { get; private set; }
    public bool IsBrowserTabSelected { get { return tabControl.SelectedContent is BrowserControl; } }

    public string Path
    {
      get { return CurrentPage != null ? CurrentPage.ToString() : string.Empty; }
      set
      {
        if (value == "http://zero-k.info/lobby/Zero-K.application") return; // this URL happens if you start installer while another copy is running. In this case dont change to this path

        if (value.ToLower().StartsWith("spring://")) value = value.Substring(9);

        var parts = value.Split('@');
        for (var i = 1; i < parts.Length; i++)
        {
          var action = parts[i];
          ActionHandler.PerformAction(action);
        }
        value = parts[0];

        if (CurrentPage != null && CurrentPage.ToString() == value) return; // we are already there, no navigation needed

        var step = GoToPage(value.Split('/'));
        if (step != null)
        {
          lastPaths.Add(value);
          if (CurrentPage != null && CurrentPage.ToString() != value) backStack.Push(CurrentPage);
          CurrentPage = step;
          //forwardStack.Clear();
        }
      }
    }

    public NavigationControl()
    {
      Buttons = new ObservableCollection<ButtonInfo>()
                {
                  new ButtonInfo() { Label = "HOME", TargetPath = "http://zero-k.info/", LinkBehavior = true},
                  new ButtonInfo() { Label = "SINGLEPLAYER", TargetPath = "http://zero-k.info/Missions", Icon = HeaderButton.ButtonIcon.Singleplayer, LinkBehavior =true },
                  new ButtonInfo() { Label = "MULTIPLAYER", TargetPath = "battles", Icon = HeaderButton.ButtonIcon.Multiplayer },
                  new ButtonInfo() { Label = "CHAT", TargetPath = "chat" },
									new ButtonInfo() { Label = "PLANETWARS", TargetPath = "http://zero-k.info/PlanetWars", LinkBehavior = true },
                  new ButtonInfo() { Label = "MAPS", TargetPath = "http://zero-k.info/Maps", LinkBehavior = true },
									new ButtonInfo() { Label = "REPLAYS", TargetPath = "http://zero-k.info/Battles", LinkBehavior = true },
                  new ButtonInfo() { Label = "SETTINGS", TargetPath = "settings" },
                };

      Instance = this;
      InitializeComponent();
    }


    public WindowsFormsHost GetWindowsFormsHostOfCurrentTab()
    {
      return tabControl.SelectedContent as WindowsFormsHost;
    }

    public bool HilitePath(string navigationPath, HiliteLevel hiliteLevel)
    {
      if (string.IsNullOrEmpty(navigationPath)) return false;
      if (hiliteLevel == HiliteLevel.Flash) foreach (var b in Buttons) if (navigationPath.StartsWith(b.TargetPath)) b.IsAlerting = true;

      var steps = navigationPath.Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
      var navigable = tabControl.Items.OfType<Object>().Select(GetINavigatableFromControl).FirstOrDefault(x => x != null && x.PathHead == steps[0]);
      if (navigable != null) return navigable.Hilite(hiliteLevel, steps);
      else return false;
    }

    public void NavigateBack()
    {
      if (CanGoBack) GoBack();
    }

    public void NavigateForward()
    {
      if (CanGoForward) GoForward();
    }

    INavigatable GetINavigatableFromControl(object obj)
    {
      if (obj is TabItem) obj = ((TabItem)obj).Content;
      if (obj is WindowsFormsHost) obj = ((WindowsFormsHost)obj).Child;
      return obj as INavigatable;
    }

    string GetLastPathStartingWith(string startString)
    {
      for (var i = lastPaths.Count - 1; i >= 0; i--) if (lastPaths[i].StartsWith(startString)) return lastPaths[i];
      return startString;
    }

    void GoBack()
    {
      if (forwardStack.Count == 0 || forwardStack.Peek().ToString() != CurrentPage.ToString()) forwardStack.Push(CurrentPage);
      CurrentPage = backStack.Pop();
      GoToPage(CurrentPage.Path);
    }

    void GoForward()
    {
      if (backStack.Count == 0 || backStack.Peek().ToString() != CurrentPage.ToString()) backStack.Push(CurrentPage);
      CurrentPage = forwardStack.Pop();
      GoToPage(CurrentPage.Path);
    }

    NavigationStep GoToPage(string[] path) // todo cleanup
    {
      foreach (var item in tabControl.Items)
      {
        var navigatable = GetINavigatableFromControl(item);
        if (navigatable != null && navigatable.TryNavigate(path))
        {
          tabControl.SelectedItem = item;
          return new NavigationStep { Path = path };
        }
      }
      return null;
    }

    public event PropertyChangedEventHandler PropertyChanged = delegate { };


    void LocationButton_Click(object sender, RoutedEventArgs e)
    {
      var buttonInfo = (ButtonInfo)((HeaderButton)sender).Tag;
      if (!buttonInfo.LinkBehavior) Path = GetLastPathStartingWith(buttonInfo.TargetPath);
      else Path = buttonInfo.TargetPath;
    }


    void UserControl_Loaded(object sender, RoutedEventArgs e)
    {
      if (string.IsNullOrEmpty(Path)) Path = "http://zero-k.info/";
    }


    void backButton_Click(object sender, RoutedEventArgs e)
    {
      NavigateBack();
    }

    void forwardButton_Click(object sender, RoutedEventArgs e)
    {
      if (CanGoForward) GoForward();
    }

    private void urlBox_GotFocus(object sender, RoutedEventArgs e) {
      urlBox.SelectAll();
    }

    private void urlBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e) {
      if (e.Key == System.Windows.Input.Key.Return) {
        Path = urlBox.Text;
        e.Handled = true;
      }
    }

    public class ButtonInfo : INotifyPropertyChanged
    {
      bool isAlerting;
      bool isSelected;
      public bool IsAlerting
      {
        get { return isAlerting; }
        set
        {
          var changed = isAlerting != value;
          isAlerting = value;
          if (changed) InvokePropertyChanged("IsAlerting");
        }
      }
      public bool IsSelected
      {
        get { return isSelected; }
        set
        {
          var changed = isSelected != value;
          isSelected = value;
          if (changed) InvokePropertyChanged("IsSelected");
        }
      }
      public string Label { get; set; }
      public HeaderButton.ButtonIcon Icon { get; set; }
      public string TargetPath;
      /// <summary>
      /// If true, lobby wont remember subpath for this button and instead go directly to target location
      /// </summary>
      public bool LinkBehavior; 
      public Visibility Visible { get; set; }

      public ButtonInfo()
      {
        Visible = Visibility.Visible;
      }

      void InvokePropertyChanged(string name)
      {
        var changed = PropertyChanged;
        if (changed != null) changed(this, new PropertyChangedEventArgs(name));
      }

      public event PropertyChangedEventHandler PropertyChanged = delegate { };
    }

    class NavigationStep
    {
      public string[] Path { get; set; }

      public override string ToString()
      {
        return string.Join("/", Path);
      }
    }

    private void hideButton_Click(object sender, RoutedEventArgs e)
    {
        //row height. Reference: http://wpftutorial.net/GridLayout.html 
        //using element type & using event to set row height. Reference: http://stackoverflow.com/questions/7334208/expanders-in-grid
        
        System.Windows.Controls.Button ex = sender as System.Windows.Controls.Button;
        System.Windows.Controls.Grid parent = FindName("bigGrid1") as System.Windows.Controls.Grid;
        if (parent.RowDefinitions[0].Height != new GridLength(2, GridUnitType.Pixel))
        {
            parent.RowDefinitions[0].Height = new GridLength(2, GridUnitType.Pixel);
            parent.RowDefinitions[1].Height = new GridLength(2, GridUnitType.Pixel);
        }
        else {
            parent.RowDefinitions[0].Height = GridLength.Auto;
            parent.RowDefinitions[1].Height = GridLength.Auto;
        }

    }

  }
}
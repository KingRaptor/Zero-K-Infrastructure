﻿using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using LobbyClient;

namespace ZeroKLobby.MicroLobby
{
  public class BattleIcon : IDisposable, IToolTipProvider, INotifyPropertyChanged
  {
    public const int Height = 75;
    public const int Width = 300;
    const int minimapSize = 58;
    bool dirty;

    bool disposed;
    Bitmap finishedMinimap;
    Bitmap image;
    bool isInGame;

    

    Bitmap playersBoxImage;
    static Size playersBoxSize = new Size(214, 32);
    Image resizedMinimap;

    public Battle Battle { get; private set; }


    public Bitmap Image
    {
      get
      {
        if (dirty)
        {
          UpdateImage();
          dirty = false;
        }
        return image;
      }
    }
    public bool IsInGame
    {
      get { return isInGame; }
      set
      {
        if (isInGame != value)
        {
          isInGame = value;
          dirty = true;
          OnPropertyChanged("BitmapSource"); // notify wpf about icon change
        }
      }
    }
    public static Size MapCellSize = new Size(74, 70);

    public Image MinimapImage
    {
      set
      {
        if (value == null)
        {
          resizedMinimap = null;
          return;
        }
        resizedMinimap = new Bitmap(value, minimapSize, minimapSize);
        dirty = true;
        OnPropertyChanged("BitmapSource"); // notify wpf about icon change
      }
    }
    public static Font ModFont = new Font("Segoe UI", 8.25F, FontStyle.Regular);
    public int PlayerCount { get { return Battle.NonSpectatorCount; } }
    public bool IsServerManaged { get; private set; }


      public static Brush TextBrush = new SolidBrush(Color.Black);
    public static Font TitleFont = new Font("Segoe UI", 8.25F, FontStyle.Bold);

    public BattleIcon(Battle battle)
    {
      Battle = battle;
      IsServerManaged = battle.Founder.IsSpringieManaged;
    }

    public void Dispose()
    {
      disposed = true;
      if (resizedMinimap != null) resizedMinimap.Dispose();
      if (playersBoxImage != null) playersBoxImage.Dispose();
      if (finishedMinimap != null) finishedMinimap.Dispose();
    }

    public bool HitTest(int x, int y)
    {
      return x > 3 && x < 290 && y > 3 && y < 64 + 3;
    }

    public void SetPlayers()
    {
      dirty = true;
      OnPropertyChanged("PlayerCount");
      OnPropertyChanged("BitmapSource"); // notify wpf about icon change
    }

    void RenderPlayers() {
      var currentPlayers = Battle.NonSpectatorCount;
      var maxPlayers = Battle.MaxPlayers;

      var friends = 0;
      var admins = 0;
      var mes = 0; // whether i'm in the battle (can be 0 or 1)

      foreach (var user in Battle.Users)
      {
        if (user.Name == Program.TasClient.UserName) mes++;
        if (Program.FriendManager.Friends.Contains(user.Name)) friends++;
        else if (user.LobbyUser.IsAdmin || user.LobbyUser.IsZeroKAdmin) admins++;
        
      }

      // make sure there aren't more little dudes than non-specs in a battle
      while (admins != 0 && friends + admins + mes > Battle.NonSpectatorCount) admins--;
      while (friends != 0 && friends + mes > Battle.NonSpectatorCount) friends--;

      if (playersBoxImage != null) playersBoxImage.Dispose();

      playersBoxImage = DudeRenderer.GetImage(currentPlayers - friends - admins - mes,
                                              friends,
                                              admins,
                                              0,
                                              maxPlayers,
                                              mes > 0,
                                              playersBoxSize.Width,
                                              playersBoxSize.Height);
    }


    void MakeMinimap()
    {
      if (resizedMinimap == null) return; // wait, map is not downloaded

      if (finishedMinimap != null) finishedMinimap.Dispose();
      finishedMinimap = new Bitmap(Resources.border.Width, Resources.border.Height);

      using (var g = Graphics.FromImage(finishedMinimap))
      {
        g.DrawImage(resizedMinimap, 6, 5);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        var x = 10;
        var y = minimapSize - 20;
        Action<Image> drawIcon = image =>
          {
            g.DrawImage(image, x, y, 20, 20);
            x += 30;
          };


        if (IsInGame) g.DrawImage(Resources.boom, 10, 10, 50, 50);
        if (Battle.IsOfficial() && Battle.Founder.IsSpringieManaged) g.DrawImage(Resources.star, 48,8,15,15); 
        if (Battle.IsPassworded) drawIcon(Resources._lock);
        if (Battle.IsReplay) drawIcon(Resources.replay);
        if (Battle.Rank > 0) drawIcon(Images.GetRank(Battle.Rank));
        if (Battle.IsLocked)
        {
          var s = 20;
          g.DrawImage(Resources.redlight, minimapSize - s + 3, minimapSize - s + 3, s, s);
        }

        g.DrawImage(Resources.border, 0, 0, 70, 70);
      }
    }

    Bitmap MakeSolidColorBitmap(Brush brush, int w, int h)
    {
      var bitmap = new Bitmap(w, h);
      try
      {
        using (var g = Graphics.FromImage(bitmap)) g.FillRectangle(brush, 0, 0, w, h);
      }
      catch
      {
        bitmap.Dispose();
        throw;
      }
      return bitmap;
    }

    void UpdateImage()
    {
      MakeMinimap();
      RenderPlayers();
      image = MakeSolidColorBitmap(Brushes.White, Width, Height);
      using (var g = Graphics.FromImage(image))
      {
        if (disposed)
        {
          image = MakeSolidColorBitmap(Brushes.White, Width, Height);
          return;
        }
        if (finishedMinimap != null) g.DrawImageUnscaled(finishedMinimap, 3, 3);
        else
        {
          g.DrawImage(Resources.download, 4, 3, 61, 64);
          g.InterpolationMode = InterpolationMode.HighQualityBicubic;
          g.InterpolationMode = InterpolationMode.Default;
        }
        g.SetClip(new Rectangle(0, 0, Width, Height));
        var y = 3;
        g.DrawString(Battle.Title, TitleFont, TextBrush, MapCellSize.Width, y + 16*0);
        g.DrawString(string.Format("{0}     {1}{2}", Battle.ModName, Battle.EngineName, Battle.EngineVersion), ModFont, TextBrush, MapCellSize.Width, y + 16*1);
        g.DrawImageUnscaled(playersBoxImage, MapCellSize.Width, y + 16*2);
        g.ResetClip();
      }
    }

    public string ToolTip { get { return ToolTipHandler.GetBattleToolTipString(Battle.BattleID); } }
    public event PropertyChangedEventHandler PropertyChanged;

    
    protected void OnPropertyChanged(string name)
    {
      PropertyChangedEventHandler handler = PropertyChanged;
      if (handler != null) {
        handler(this, new PropertyChangedEventArgs(name));
      }
    }
  }
}
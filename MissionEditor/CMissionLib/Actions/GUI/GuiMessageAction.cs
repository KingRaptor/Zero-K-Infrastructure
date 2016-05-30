﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Windows.Media.Imaging;

namespace CMissionLib.Actions
{
	[DataContract]
	public class GuiMessageAction : Action, ILocalizable
	{
        string stringID;
		string imagePath;
		string message;
		int width = 400;
        int height = 300;
        int fontSize = 14;

		public GuiMessageAction(string message)
		{
			this.message = message;
			Pause = true;
		}

        [DataMember]
        public string StringID
        {
            get { return stringID; }
            set
            {
                stringID = value;
                RaisePropertyChanged("StringID");
            }
        }

        [DataMember]
		public string Message
		{
			get { return message; }
			set
			{
				message = value;
				RaisePropertyChanged("Message");
			}
		}

		[DataMember]
		public bool Pause { get; set; }

		[DataMember]
		public string ImagePath
		{
			get { return imagePath; }
			set
			{
				imagePath = value;
				RaisePropertyChanged("ImagePath");
			}
		}

		[DataMember]
		public int Width
		{
			get { return width; }
			set
			{
				width = value;
				RaisePropertyChanged("Width");
			}
		}

        [DataMember]
        public int Height
        {
            get { return height; }
            set
            {
                height = value;
                RaisePropertyChanged("Height");
            }
        }

        [DataMember]
        public int FontSize
        {
            get { return fontSize; }
            set
            {
                fontSize = value;
                RaisePropertyChanged("FontSize");
            }
        }
		public override LuaTable GetLuaTable(Mission mission)
		{
			if (string.IsNullOrEmpty(imagePath) || !File.Exists(ImagePath))
			{
				var map = new Dictionary<object, object>
					{
                        {"stringID", stringID},
                        {"message", message},
						{"width", Width},
                        {"height", Height},
						{"pause", Pause},
                        {"fontSize", FontSize},
					};
                if(!string.IsNullOrWhiteSpace(imagePath))
                {
                    map.Add("image", imagePath);
                    map.Add("imageFromArchive", true);
                }
				return new LuaTable(map);
			}
			else
			{
				var image = new BitmapImage(new Uri(ImagePath));
				var map = new Dictionary<object, object>
					{
						{"message", message},
						{"image", Path.GetFileName(ImagePath)},
						{"imageWidth", image.PixelWidth},
						{"imageHeight", image.PixelHeight},
						{"pause", Pause},
                        {"fontSize", FontSize},
					};
				return new LuaTable(map);
			}
		}

		public override string GetDefaultName()
		{
			return "GUI Message";
		}
	}
}
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.IO;
using Microsoft.Win32;
using System.ComponentModel;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace ASBRedactor
{
    /// <summary>
    /// Логика взаимодействия для MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private BindingList<TextLine> texts = new BindingList<TextLine>();
        private List<TextLine> systemVars = new List<TextLine>();
        private string filename;
        private byte[] file;
        public class TextLine
        {
            public string Text { get; set; }
            public int OffsetPos { get; set; }
            public int OldAddr { get; set; }
        }
        public MainWindow()
        {
            InitializeComponent();
        }
        public BindingList<TextLine> Texts
        {
            get => texts; set
            {
                texts = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Texts)));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private bool ArraysEqual(byte[] source, int start, byte[] find)
        {
            if (start + find.Length >= source.Length)
            {
                return false;
            }
            for (int i = 0; i < find.Length; i++)
            {
                if (source[start + i] != find[i])
                {
                    return false;
                }
            }
            return true;
        }
        private int ReadInt32(byte[] file, int offset) => BitConverter.ToInt32(file, offset);
        private void LoadTexts(byte[] bytes)
        {
            var data_offset = ReadInt32(bytes, 0x34);
            var code_start = ReadInt32(bytes, 0x2c);
            var code_length = ReadInt32(bytes, 0x30);
            var list = bytes.ToList();
            var start = data_offset;
            Texts.Clear();
            while (true)
            {
                var pos = list.IndexOf(0, start);
                if(pos == -1)
                {
                    break;
                }
                var buff = new byte[pos - start];
                Array.Copy(bytes, start, buff, 0, pos - start);
                var str = LoadString(buff, 0);
                if (str[0] == '@')
                {
                    start = pos + 1;
                    continue;
                }
                if (str == "__main")
                {
                    start = pos + 4;
                    continue;
                }
                var offset = FindBytes(bytes, BitConverter.GetBytes(start-data_offset));
                if(offset == -1)
                {
                    MessageBox.Show("Found text without link");
                }
                Texts.Add(new TextLine {Text=str,OffsetPos=offset,OldAddr=start});
                if (str == "__main") {
                    start = pos + 4;
                }
                else {
                    start = pos + 1;
                }
            }
        }
        private int FindBytes(byte[] bytes, byte[] findable)
        {
            var code_start = ReadInt32(bytes, 0x2c);
            var code_length = ReadInt32(bytes, 0x30);
            for (int i = code_start; i < code_start + code_length; i++)
            {
                if (ArraysEqual(bytes, i, findable))
                {
                    return i;
                }
            }
            return -1;
        }
        /*private void LoadTexts(byte[] bytes)
        {
            Texts.Clear();
            var data_offset = ReadInt32(bytes, 0x34);
            var code_start = ReadInt32(bytes, 0x2c);
            var code_length = ReadInt32(bytes, 0x30);
            var findable = new byte[] { 0x62, 0x02, 0x29, 0x1d };
            var findable2 = new byte[] { 0x62, 0x02, 0x29, 0x1e };
            for (int i = code_start; i < code_start + code_length; i++)
            {
                if (ArraysEqual(bytes, i, findable) || ArraysEqual(bytes,i,findable2))
                {
                    var offset_pos = i + 9;
                    var offset = ReadInt32(bytes, offset_pos) + data_offset;

                    var temp = LoadString(bytes, offset);
                    foreach(var item in Texts)
                    {
                        if(item.Text == temp)
                        {
                            Console.WriteLine("Copy");
                        }
                    }
                    Texts.Add(new TextLine { Text = temp, OffsetPos = offset_pos,OldAddr=offset });
                }
            }
        }*/
        private string LoadString(byte[] bytes, int offset)
        {
            int length = 256;
            if (offset + length >= bytes.Length)
            {
                length = bytes.Length - offset;
            }
            var buff = new byte[length];

            Array.Copy(bytes, offset, buff, 0, length);
            return Encoding.GetString(buff.TakeWhile(x => x != 0).ToArray());
        }
        private void LoadSystemVar(byte[] bytes)
        {
            systemVars.Clear();
            var data_offset = ReadInt32(bytes, 0x34);
            var var_offset = ReadInt32(bytes, 0x24);
            var var_count = ReadInt32(bytes, 0x28);
            for (int i = 0; i < var_count; i++)
            {
                var offset_pos = var_offset + i * 20;
                var offset = ReadInt32(bytes, offset_pos) + data_offset;
                var temp = LoadString(bytes, offset);
                systemVars.Add(new TextLine { Text = temp, OffsetPos = offset_pos,OldAddr=offset });
            }
        }
        private void OpenCommandBinding_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            var win = new OpenFileDialog();
            if (!(bool)win.ShowDialog())
            {
                return;
            }
            var path = win.FileName;
            var bytes = File.ReadAllBytes(path);

            LoadTexts(bytes);
            LoadSystemVar(bytes);
            filename = path;
            file = bytes;
        }
        static readonly Encoding Encoding = Encoding.GetEncoding("shift-jis");
        class Item
        {
            public int addr_offset;
            public int old_addr;
            public byte[] bytes;
            public string text;
            public Item(TextLine text)
            {
                addr_offset = text.OffsetPos;
                bytes = Encoding.GetBytes(text.Text);
                old_addr = text.OldAddr;
                this.text = text.Text;
            }
        }
        private void SaveCommandBinding_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            var data_offset = ReadInt32(file, 0x34);
            var list = new List<Item>();

            var ind = systemVars.FindIndex(x => x.Text == "__main");
            if (ind != -1)
            {
                list.Add(new Item(systemVars[ind]));
            }
            list[0].bytes = list[0].bytes.Concat(new byte[] { 0, 0x81, 0x40 }).ToArray();
            foreach (var item in systemVars.Where(x => x.Text != "__main"))
            {
                list.Add(new Item(item));
            }
            foreach (var item in Texts)
            {
                list.Add(new Item(item));
            }
            var data_length = list.Sum(x => x.bytes.Length + 1);
            var temp = new byte[data_offset + data_length];
            Array.Copy(file, 0, temp, 0, data_offset);
            int addr = 0;
            foreach(var item in list)
            {
                Array.Copy(item.bytes, 0, temp, addr + data_offset, item.bytes.Length);
                var bytes = BitConverter.GetBytes(addr);
                Array.Copy(bytes, 0, temp, item.addr_offset, bytes.Length);
                addr += item.bytes.Length + 1;
            }
            File.WriteAllBytes(filename, temp);
        }
    }
}

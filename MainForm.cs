using System;
using System.Drawing;
using System.Windows.Forms;
using System.Text;
using System.IO;

namespace proxy
{

public partial class MainForm : Form
{
	// Program で Dispose させるため、public にしている
	public static Font ms_meiryo_8pt;

	public MainForm()
	{
		InitializeComponent();

		ms_meiryo_8pt = new Font("メイリオ", 8.25F, FontStyle.Regular, GraphicsUnit.Point, ((byte)(128)));

//		m_btn_clearLog.Font = ms_meiryo_Ke_P_9pt;

		// ---------------------------------------------------------
		m_rbox_stdout.Font = ms_meiryo_8pt;
		m_rbox_stdout.SelectionTabs = new int[] { 30 };
		m_rbox_stdout.LanguageOption = RichTextBoxLanguageOptions.UIFonts;  // 行間を狭くする

		m_rbox_study.Font = ms_meiryo_8pt;
		m_rbox_study.SelectionTabs = new int[] { 30 };
		m_rbox_study.LanguageOption = RichTextBoxLanguageOptions.UIFonts;  // 行間を狭くする

		// ---------------------------------------------------------
		m_rbox_study.AllowDrop = true;
		m_rbox_study.DragDrop += MainForm_DragDrop;
		m_rbox_study.DragEnter += MainForm_DragEnter;

		m_btn_clearLog.Click += KLog.ClrScreen;
		m_btn_stop_svr.Click += OnClk_StopSvr;
		KLog.Set_rbox(m_rbox_stdout);

		m_btn_clearStudyLog.Click += KLog_Study.ClrScreen;
		KLog_Study.Set_rbox(m_rbox_study);

		Server.Start();
	}

	async void OnClk_StopSvr(object sender, EventArgs e)
	{
		await Server.StopServer();
	}

	// ------------------------------------------------------------------------------------
	private void MainForm_DragDrop(object sender, DragEventArgs e)
	{
		string[] files = (string[])e.Data.GetData(DataFormats.FileDrop, false);

		foreach (var file_name in files)
		{
			KLog_Study.Wrt_BkRed(file_name + "\r\n");
			var fs = new FileStream(file_name, FileMode.Open, FileAccess.Read, FileShare.None);
			int file_bytes = (int)fs.Length;
			byte[] ary_buf_file = new byte[file_bytes];

			fs.Read(ary_buf_file, 0, file_bytes);
			fs.Dispose();

			var res_reader = new Response_Reader(ary_buf_file);
			res_reader.ShowToLog(file_bytes);
		}
	}

	private void MainForm_DragEnter(object sender, DragEventArgs e)
	{
		if (e.Data.GetDataPresent(DataFormats.FileDrop))
		{
			e.Effect = DragDropEffects.Copy;
		}
		else
		{
			e.Effect = DragDropEffects.Copy;
		}
	}
} // class MainForm

	///////////////////////////////////////////////////////////////////////////////////////

	static class KLog
{
	static RichTextBox ms_rbox_stdout = null;	

	public static void Set_rbox(RichTextBox rbox)
	{
		ms_rbox_stdout = rbox;
	}

	public static void Write(string msg)
	{
		ms_rbox_stdout.AppendText(msg);
	}

	public static void Wrt_BkBlue(string msg)
	{
		ms_rbox_stdout.SelectionBackColor = Color.FromArgb(220, 220, 255);
		ms_rbox_stdout.AppendText(msg);
		ms_rbox_stdout.SelectionBackColor = Color.White;
	}

	public static void Wrt_BkRed(string msg)
	{
		ms_rbox_stdout.SelectionBackColor = Color.FromArgb(255, 220, 220);
		ms_rbox_stdout.AppendText(msg);
		ms_rbox_stdout.SelectionBackColor = Color.White;
	}

	public static void ClrScreen(object sender, EventArgs e)
	{
		ms_rbox_stdout.Clear();
	}

} // class G_Log 

///////////////////////////////////////////////////////////////////////////////////////

static class KLog_Study
{
	static RichTextBox ms_rbox_study = null;	

	public static void Set_rbox(RichTextBox rbox)
	{
		ms_rbox_study = rbox;
	}

	public static void Write(string msg)
	{
		ms_rbox_study.AppendText(msg);
	}

	public static void Wrt_BkBlue(string msg)
	{
		ms_rbox_study.SelectionBackColor = Color.FromArgb(220, 220, 255);
		ms_rbox_study.AppendText(msg);
		ms_rbox_study.SelectionBackColor = Color.White;
	}

	public static void Wrt_BkRed(string msg)
	{
		ms_rbox_study.SelectionBackColor = Color.FromArgb(255, 220, 220);
		ms_rbox_study.AppendText(msg);
		ms_rbox_study.SelectionBackColor = Color.White;
	}

	public static void Wrt_CR()
	{
		ms_rbox_study.AppendText("\r\n");
	}

	public static void ClrScreen(object sender, EventArgs e)
	{
		ms_rbox_study.Clear();
	}

} // class KLog_Study

// ---------------------------------------------------------
class File_Log : IDisposable
{
	const int EN_thld_mem_buf = 1024;  // メモリストリームの閾値
	const int EN_size_mem_buf = 2 * 1024;  // メモリストリームサイズの初期値
	const int EN_size_file_buf = 10 * 1024;  // フィアルストリームサイズの初期値（10 kbytes）

	static MemoryStream ms_ms = new MemoryStream(EN_size_mem_buf);
	static FileStream ms_fs = null;

	static object ms_lockObj = new object();

	public File_Log()
	{
		// まず、ファイル名を決定する（１日１ファイルにしておこうと思う。Md_Svr_yy-mm-dd.txt）
		string fpath = $"z:/Proxy_{DateTime.Now.ToString("HH-mm-ss")}.txt";

		ms_fs = new FileStream(fpath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite, EN_size_file_buf);

		// ファイルは追記していくことにする
		ms_fs.Position = ms_fs.Length;
	}

	// メモリストリームの内容が閾値を超えていたらファイルストリームに書き出す
	static void CheckWrtOut()
	{
		if (ms_ms.Position > EN_thld_mem_buf)
		{
			ms_ms.WriteTo(ms_fs);
			ms_ms.Position = 0;
			ms_ms.SetLength(0);

			ms_fs.Flush();  /// TODO: filestream を Flush させる頻度は、今後調整すること
		}
	}

	public void Dispose()
	{
		if (ms_ms.Position > 0)
		{
			ms_ms.WriteTo(ms_fs);
			ms_ms.Position = 0;
		}
		ms_fs.Flush();

		ms_fs.Dispose();
		ms_ms.Dispose();
	}

	// ---------------------------------------------------------
	public void Wrt_Block(byte[] ary_buf, int pos, int len)
	{
		if (ms_ms.Position > 0)
		{
			ms_ms.WriteTo(ms_fs);
			ms_ms.Position = 0;
			ms_ms.SetLength(0);
		}

		ms_fs.Write(ary_buf, pos, len);
		ms_fs.Flush();
	}

	// ---------------------------------------------------------
	// 以下のメソッドを利用する
	static public void Wrt(string str)
	{
		lock (ms_lockObj)
		{
			byte[] ary_buf = Encoding.UTF8.GetBytes(str);
			ms_ms.Write(ary_buf, 0, ary_buf.Length);
			CheckWrtOut();
		}
	}

	static public void Wrt_Time(string str)
	{
		lock (ms_lockObj)
		{
			byte[] ary_buf = Encoding.UTF8.GetBytes(DateTime.Now.ToString("[HH:mm:ss]　") + str);
			ms_ms.Write(ary_buf, 0, ary_buf.Length);
			CheckWrtOut();
		}
	}

	static public void Wrt_SIG_Time(string str)
	{
		lock (ms_lockObj)
		{
			byte[] ary_buf = Encoding.UTF8.GetBytes("\r\n" + DateTime.Now.ToString("[HH:mm:ss]　") + str);
			ms_ms.Write(ary_buf, 0, ary_buf.Length);
			CheckWrtOut();
		}
	}

	static public void Wrt_ERR_Time(string str)
	{
		lock (ms_lockObj)
		{
			byte[] ary_buf = Encoding.UTF8.GetBytes("\r\n" + DateTime.Now.ToString("[HH:mm:ss]　") + str + "\r\n");
			ms_ms.Write(ary_buf, 0, ary_buf.Length);
			CheckWrtOut();
		}
	}
}

} // namespace proxy

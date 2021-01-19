using System;
using System.Text;

namespace proxy
{

class ReqHead_Reader
{
	public enum ID_Method
	{
		Unknown,

		CONNECT,
		GET_http,
		GET_https,
	}

	public enum ID_Field
	{
		Unknown,

		Host,
		UserAgent,
		Accept,
		AcceptLang,
		AcceptEnc,
		DNT,
		Connection,
		UpgradeInsecure,

		END,  // 空行
	}

	static uint ms_uistr_Host = 0, ms_uistr_UserAgent, ms_uistr_Accept, ms_uistr_AcceptLang
			, ms_uistr_AcceptEnc, ms_uistr_DNT, ms_uistr_Connection, ms_uistr_UpgradeInsecure;

	// ----------------------------------------------------
	public const int EN_bytes_ReqHead_Buf = 2048;

	// CltContext.ary_buf_clt_req_head が、途中で途切れている時に利用される。通常は空。
	int m_idx_ary_buf_Clt_req_head = 0;
	byte[] m_ary_buf_Clt_req_head = new byte[EN_bytes_ReqHead_Buf];
	
	// ターゲットに送信すべきヘッダが生成されるバッファ
	// m_idx_ary_buf_Tgt_req_head は、ary_buf_clt_req_head が途中で途切れてるときにのみ利用される
//	int m_idx_ary_buf_Tgt_req_head = 0;  // ターゲットへのリクエストヘッダを追記する時に利用
	byte[] m_ary_buf_Tgt_req_head = new byte[EN_bytes_ReqHead_Buf];

	byte[] m_ary_buf_CltContext;

	static byte[] ms_ary_buf_404_not_found = null;

	// ------------------------------------------------------------------------------------
	public ReqHead_Reader(byte[] ary_buf_CltContext)
	{
		m_ary_buf_CltContext = ary_buf_CltContext;

		if (ms_ary_buf_404_not_found == null)
		{
			ms_ary_buf_404_not_found = Encoding.UTF8.GetBytes("HTTP/1.1 404 Not Found\r\n"
				+ "\r\n<html><body><h1>404 Not Found</h1></html>");
		}

		// ----------------------------------------------------
		if (ms_uistr_Host == 0)
		{
			ms_uistr_Host = Uistr.S_4chr("host");
			ms_uistr_UserAgent = Uistr.S_4chr("user");
			ms_uistr_Accept = Uistr.S_4chr("acce");
			ms_uistr_AcceptLang = Uistr.S_4chr("lang");
			ms_uistr_AcceptEnc = Uistr.S_4chr("enco");
			ms_uistr_DNT = Uistr.S_4chr("dnt:");
			ms_uistr_Connection = Uistr.S_4chr("conn");
			ms_uistr_UpgradeInsecure = Uistr.S_4chr("upgr");
		}
	}

	public static byte[] Get_ary_buf_res_404() => ms_ary_buf_404_not_found;

	// ------------------------------------------------------------------------------------
	// 戻り値: 送信すべき ReqHead の byte[] と、そのバイト数
	// もし、バイト数が -1 である場合は、404 などをクライアントに返すためのバッファが返されている。
	// その場合は、byte[] 全てをクライアントに送信すれば良い。
	// 戻り値の ary_buf_Tgt_req_head が null の場合は、ReadAsync の続きが必要であることを表している
	public unsafe (byte[] ary_buf_Tgt_req_head, int bytes, string host_name) Consume(int bytes_recv)
	{
		if (m_idx_ary_buf_Clt_req_head > 0)
		{
			throw new Exception("!!! m_idx_ary_buf_Clt_req_head > 0 のときの動作は未実装");
		}

//		m_idx_ary_buf_Tgt_req_head = 0;  // ターゲットへのリクエストヘッダを追記する時に利用
		fixed (byte* pbuf_clt_context = m_ary_buf_CltContext)
		fixed (byte* pdst_tgt_req_head = m_ary_buf_Tgt_req_head)
		{
			byte* pTmnt_src = pbuf_clt_context + bytes_recv;
			byte* psrc = pbuf_clt_context;
			byte* pdst = pdst_tgt_req_head;

			// pdst, psrc 共に、１行目が処理されて、次の行頭を指したところでリターンしてくる
			var (id_method, host_name) = Consume_Method(ref pdst, ref psrc, pTmnt_src);

			KLog.Wrt_BkRed($"メソッド -> {id_method.ToString()}\r\n");

			if (id_method != ID_Method.GET_http)
			{ return (ms_ary_buf_404_not_found, -1, null); }

			// 現時点では、id_method == GET_http のときのみを扱う
			// 残りの行を全てコピーすれば良いが、今は勉強のために全て解析するようにしている
			KLog_Study.Wrt_BkBlue("==============================\r\n");
			KLog_Study.Wrt_BkRed(host_name + "\r\n");

			// メソッド行の表示
			string str_1Line = DBG_Get_1Line(pdst_tgt_req_head);
			KLog_Study.Write(str_1Line);

			while (true)
			{
				if (psrc >= pTmnt_src)
				{ throw new Exception("!!! 読み取りバッファオーバーランを検出"); }

				// Filed 行の処理
				byte* DBG_ptop_1Line = pdst;
				ID_Field id_field = Consume_1Line(ref pdst, ref psrc);
				if (id_field == ID_Field.Unknown)
				{
					KLog_Study.Wrt_BkRed("リクエストヘッダに不明なフィールドを検出\r\n");
					string unknown_field = DBG_Get_1Line(psrc);
					KLog_Study.Wrt_BkRed(unknown_field);
					throw new Exception("http リクエストヘッダに、不明なものを検出しました。");
				}

				KLog_Study.Wrt_BkBlue(id_field.ToString());
				if (id_field == ID_Field.DNT) { KLog_Study.Wrt_CR(); continue; }
				if (id_field == ID_Field.END) { break; }

				str_1Line = DBG_Get_1Line(DBG_ptop_1Line);
				KLog_Study.Write(str_1Line);
			}

			// Content の処理
			KLog_Study.Wrt_BkRed("\r\nContents -> ");
			KLog_Study.Write((pTmnt_src - psrc).ToString() + " bytes\r\n");
			byte* pTmnt_dst = pdst_tgt_req_head + EN_bytes_ReqHead_Buf;
			while (true)
			{
				if (psrc == pTmnt_src) { break; }

				if (pdst >= pTmnt_dst)
				{ throw new Exception("ReqHead_Reader.Consume() : pdst >= pTmnt_dst"); }

				*pdst++ = *psrc++;
			}

			return (m_ary_buf_Tgt_req_head, (int)(pdst - pdst_tgt_req_head), host_name);
		}
	}

	// ------------------------------------------------------------------------------------
	// pdata には、データ領域の先頭アドレスが返される
	// .net framework では、タプルでポインタを扱えない。
	// そのため、分かりにくくなるが、ref で psrc を受け取ることにした。
	// pdst には、メソッドを指定する最初の１行が設定される。（末尾に \r\n も設定される）
	unsafe (ID_Method, string host_name) Consume_Method(ref byte* pdst, ref byte* psrc, byte* pTmnt_src)
	{
		switch (*(uint*)psrc)
		{
		case 0x4e_4e_4f_43:  // "CONN"
			psrc += 4;
			while (*psrc++ != ' ') {}
			return (ID_Method.CONNECT, null);

		case 0x20_54_45_47:  // "GET "
			psrc += 3;
			while (*psrc++ != ' ') {}
			if (*(uint*)psrc != 0x70_74_74_68)  // "http"
			{
				return (ID_Method.Unknown, null);
			}

			if (*(psrc + 4) == 's')
			{
				// 現時点では処理対象外
				throw new Exception("GET リクエストで、https を受け取りました。");
			}

			psrc += 7;
			string host_name = GetHostName_SetMethodToTgt(ref pdst, ref psrc, pTmnt_src);
			return (ID_Method.GET_http, host_name);
		}

		return (ID_Method.Unknown, null);
	}

	// ------------------------------------------------------------------------------------
	// psrc は、「http://」の次のアドレスを指している
	// 戻り値は、tgt_tcp_client の DNS名
	unsafe string GetHostName_SetMethodToTgt(ref byte* pdst, ref byte* psrc, byte* pTmnt_src)
	{
		// psrc の host 名を取得（tgt_tcp_client の生成のため）
		byte* ptop_host_name = psrc;
		while (true)
		{
			if (psrc >= pTmnt_src)
			{ throw new Exception("読み取りバッファオーバーランを検出"); }

			if (*psrc == '/') { break; }
			psrc++;
		}

		// *psrc == '/'
		string str_tgt_dns = Encoding.UTF8.GetString(ptop_host_name, (int)(psrc - ptop_host_name));

		*(uint*)pdst = 0x20_54_45_47;  // "GET "
		pdst += 4;

		while (true)
		{
			if (psrc >= pTmnt_src)
			{ throw new Exception("読み取りバッファオーバーランを検出"); }

			if (*psrc == '\n') { break; }
			*pdst++ = *psrc++;
		}

		*pdst++ = *psrc++;  // '\n' のコピー
		return str_tgt_dns;
	}

	// ------------------------------------------------------------------------------------
	//「改行を含めて」文字列を取り出す
	// 空行の場合は、null が返される
	const int EN_MAX_len_1Line = 300;  // ptr が、contents 部分を指していても大丈夫なようにするための措置
	unsafe string DBG_Get_1Line(byte* ptr)
	{
		byte* ptop = ptr;
		int cnt = EN_MAX_len_1Line;

		while (true)
		{
			byte chr = *ptr;
			if (chr == '\r' || chr == '\n')
			{
				if (ptr == ptop) { return null; }
				if (chr == '\r') { ptr++; }
				break;
			}

			if (--cnt == 0)
			{ throw new Exception("!!! ReqHead_Reader.DBG_Get_1Line() : 読み取りオーバー発生"); }

			ptr++;
		}

		// ptr は '\n' の位置を指している
		return Encoding.UTF8.GetString(ptop, (int)(ptr - ptop + 1));  // +1 : \n を含ませる
	}

	// ------------------------------------------------------------------------------------
	// 改行も含めてコピーを実行する
	unsafe void Copy_1Line(ref byte* pdst, ref byte* psrc)
	{
		int cnt = EN_MAX_len_1Line;

		while ((*pdst++ = *psrc++) != '\n')
		{
			if (--cnt == 0)
			{ throw new Exception("!!! ReqHead_Reader.Copy_1Line() : 読み取りオーバー発生"); }
		}
	}

	// ------------------------------------------------------------------------------------
	// 改行も含めて skip する
	unsafe void Skip_1Line(ref byte* psrc)
	{
		int cnt = EN_MAX_len_1Line;

		while (*psrc++ != '\n')
		{
			if (--cnt == 0)
			{ throw new Exception("!!! ReqHead_Reader.Skip_1Line() : 読み取りオーバー発生"); }
		}
	}

	// ------------------------------------------------------------------------------------
	unsafe ID_Field Consume_1Line(ref byte* pdst, ref byte* psrc)
	{
		uint uistr_src = *(uint*)psrc | 0x2020_2020;  // 大文字 -> 小文字
		if (uistr_src == ms_uistr_Host)
		{
			Copy_1Line(ref pdst, ref psrc);
			return ID_Field.Host;
		}
		if (uistr_src == ms_uistr_UserAgent)
		{
			Copy_1Line(ref pdst, ref psrc);
			return ID_Field.UserAgent;
		}
		if (uistr_src == ms_uistr_Accept)
		{
			if (*(psrc + 6) == ':')
			{
				Copy_1Line(ref pdst, ref psrc);
				return ID_Field.Accept;
			}
			uint usstr_src_2 = *(uint*)(psrc + 7) | 0x2020_2020;  // 大文字 -> 小文字
			if (usstr_src_2 == ms_uistr_AcceptLang)
			{
				Copy_1Line(ref pdst, ref psrc);
				return ID_Field.AcceptLang;
			}
			if (usstr_src_2 == ms_uistr_AcceptEnc)
			{
				Copy_1Line(ref pdst, ref psrc);
				return ID_Field.AcceptEnc;
			}
		}
		if (uistr_src == ms_uistr_DNT)
		{
			Skip_1Line(ref psrc);
			return ID_Field.DNT;
		}
		if (uistr_src == ms_uistr_Connection)
		{
			Copy_1Line(ref pdst, ref psrc);
			return ID_Field.Connection;
		}
		if (uistr_src == ms_uistr_UpgradeInsecure)
		{
			Copy_1Line(ref pdst, ref psrc);
			return ID_Field.UpgradeInsecure;
		}
		if (*psrc == '\n')
		{
			psrc++;

			*(ushort*)pdst = 0x0a_0d;
			pdst += 2;
			return ID_Field.END;
		}
		if (*psrc == '\r')
		{
			psrc += 2;

			*(ushort*)pdst = 0x0a_0d;
			pdst += 2;
			return ID_Field.END;
		}

		return ID_Field.Unknown;
	}

} // ReqHead_Reader
} // namespace proxy

using System;
using System.Text;

namespace proxy
{
class Response_Reader
{
	public enum RET
	{
		Detect_Header_End,
		Consume_1Line,
		Detect_Line_Fragment,
	}

	static ulong ms_ui64str_Content = 0;
	static uint ms_uistr_Enc, ms_uistr_Length, ms_uistr_gzip;

	// ----------------------------------------------------
	byte[] m_ary_buf_response;
	int m_idx_consume = 0;
	int m_valid_bytes_cur_buf = 0;  // バッファ内の有効バイト数

	// ContentLengthInfo
	bool mb_enc_gzip = false;
	int m_rem_content_length = -1;  // この値は減算されて利用される
	int m_rem_chunk_cur = -1;  // この値は減算されて利用される（0 の場合もあり得る。(空行を読み出せていない場合)）

	// ------------------------------------------------------------------------------------
	public Response_Reader(byte[] ary_buf_response)
	{
		m_ary_buf_response = ary_buf_response;

		if (ms_ui64str_Content == 0)
		{
			ms_ui64str_Content = Uistr.S_8chr("content-");

			ms_uistr_Enc = Uistr.S_4chr("enco");
			ms_uistr_Length = Uistr.S_4chr("leng");
			ms_uistr_gzip = Uistr.S_4chr("gzip");
		}
	}

	// ------------------------------------------------------------------------------------
	public int Get_idx_ary_buf_cur() => m_idx_consume;

	void Reset_ContentLengthInfo()
	{
		mb_enc_gzip = false;
		m_rem_content_length = -1;
		m_rem_chunk_cur = -1;
	}
	
	// ------------------------------------------------------------------------------------
	// 戻り値： ReadAsync() で指定する offset 値
	public int Prepare_ReadAsync()
	{
		if (m_idx_consume == m_valid_bytes_cur_buf)
		{
			m_idx_consume = 0;
			m_valid_bytes_cur_buf = 0;
			return 0;
		}
		else
		{
			int rem_bytes = m_valid_bytes_cur_buf - m_idx_consume;
			//+++++ エラー顕在化
			if (rem_bytes < 0)
			{ throw new Exception("m_valid_bytes_cur_buf < m_idx_consume を検出しました。"); }

			// m_idx_consume < m_valid_bytes_cur_buf であるときの処理
			unsafe
			{
				fixed (byte* ary_buf = m_ary_buf_response)
				{
					ulong* pdst = (ulong*)ary_buf;
					ulong* psrc = (ulong*)(ary_buf + m_idx_consume);
					for (uint i = ((uint)(rem_bytes + 7) >> 3); i > 0; --i)
					{ *pdst++ = *psrc++; }
				}
			}

			m_valid_bytes_cur_buf = rem_bytes;
			m_idx_consume = 0;

			return rem_bytes;
		}
	}

	// ------------------------------------------------------------------------------------
	public void OnReadAsync(int bytes_recv)
	{
		m_valid_bytes_cur_buf += bytes_recv;
	}

	// ------------------------------------------------------------------------------------
	// 戻り値： bool -> レスポンスヘッダの読み取りが完了した場合は ture、そうでない場合は false
	// byte -> consume したバイト数（空行を含む。content 直前までのバイト数）
	public unsafe (bool, int bytes) Consume_Header()
	{
		//+++++ エラー顕在化
		if (m_idx_consume >= m_valid_bytes_cur_buf)
		{ throw new Exception("m_idx_consume >= m_valid_bytes_cur_buf の状態で Consume_Header() がコールされました。"); }

		int idx_ary_buf_before_consume = m_idx_consume;
		RET ret;
		fixed (byte* pary_buf_res = m_ary_buf_response)
		{
			byte* ptr = pary_buf_res + m_idx_consume;
			byte* pTmnt = pary_buf_res + m_valid_bytes_cur_buf;

			while (true)
			{
				ret = Consume_1Line(ref ptr, pTmnt);
				if (ret == RET.Consume_1Line) { continue; }
				break;
			}
			// ret == Detect_Header_End or Detect_Line_Fragment

			// ret == Detect_Header_End -> m_idx_consume は、コンテンツの先頭
			// ret == Detect_Line_Fragment -> m_idx_consume は、読み取り失敗した行頭（pTmnt の位置のときもある）
			m_idx_consume = (int)(ptr - pary_buf_res);
		}
		int bytes_consume = m_idx_consume - idx_ary_buf_before_consume;

		if (ret == RET.Detect_Line_Fragment) { return (false, bytes_consume); }
		
		// ここに来るのは、ret == Detect_Header_End であるとき
		return (true, bytes_consume);
	}

	// ------------------------------------------------------------------------------------
	// Detect_Line_Fragment が返されたときは、ptr の値は変更されていない
	unsafe RET Consume_1Line(ref byte* ptr, byte* pTmnt)
	{
		// エラー顕在化
		if (ptr > pTmnt) { throw new Exception("読み取りバッファオーバーランを検出"); }

		if (ptr == pTmnt) { return RET.Detect_Line_Fragment; }
		// 以下では ptr < pTmnt となっている

		if (*ptr == '\n')
		{
			ptr++;
			return RET.Detect_Header_End;
		}
		if (*ptr == '\r')
		{
			if (ptr + 1 == pTmnt)
			{
				return RET.Detect_Line_Fragment;
			}

			ptr += 2;
			return RET.Detect_Header_End;
		}

		if (ptr + 8 > pTmnt) { return RET.Detect_Line_Fragment; }

		// ---------------------------------------------------------
		byte* ptr_for_rollback = ptr;

		ulong ui64str = *(ulong*)ptr | 0x2020_2020_2020_2020;  // 大文字 -> 小文字
		// content- の処理
		if (ui64str == ms_ui64str_Content)
		{
			if (ptr + 12 > pTmnt) { return RET.Detect_Line_Fragment; }

			uint uistr = *(uint*)(ptr + 8) | 0x2020_2020;  // 大文字 -> 小文字
			if (uistr == ms_uistr_Enc)
			{
				// Content-Encoding:
				// 23 = 17(content-encoding:) + 4(gzip) + 2(\r\n)
				if (ptr + 23 > pTmnt) { return RET.Detect_Line_Fragment; }

				ptr += 17;
				while (true)
				{
					if (ptr == pTmnt) { ptr = ptr_for_rollback; return RET.Detect_Line_Fragment; }
					if (*ptr != ' ') { break; }
					ptr++;
				}

				// 6 = 4(gzip) + 2(\r\n)
				if (ptr + 6 > pTmnt) { ptr = ptr_for_rollback; return RET.Detect_Line_Fragment; }

				if (*(uint*)ptr == ms_uistr_gzip)
				{
					// Content-Encoding: gzip
					mb_enc_gzip = true;
					ptr += 4;
				}
			}
			else if (uistr == ms_uistr_Length)
			{
				// Content-Length:
				// 23 = 15(content-length:) + 1文字以上 + 2(\r\n)
				if (ptr + 18 > pTmnt) { return RET.Detect_Line_Fragment; }

				ptr += 15;
				while (true)
				{
					if (ptr == pTmnt) { ptr = ptr_for_rollback; return RET.Detect_Line_Fragment; }
					if (*ptr != ' ') { break; }
					ptr++;
				}

				// Length の取得
				int len = 0;
				while (true)
				{
					if (ptr == pTmnt) { ptr = ptr_for_rollback; return RET.Detect_Line_Fragment; }
					int dig = *ptr++;
					if (dig == '\r')
					{
						if (ptr == pTmnt) { ptr = ptr_for_rollback; return RET.Detect_Line_Fragment; }

						ptr++;
						m_rem_content_length = len;
						return RET.Consume_1Line;
					}
					if (dig == '\n')
					{
						m_rem_content_length = len;
						return RET.Consume_1Line;
					}

					if (dig < '0' || dig > '9')
					{ throw new Exception("content-length に数字以外の文字を検出しました。"); }

					len = len * 10 + (dig - 0x30);
				}
			}
		} // content- の処理
		// ---------------------------------------------------------

		// 次の行頭へ移動
		while (true)
		{
			if (ptr == pTmnt) { ptr = ptr_for_rollback; return RET.Detect_Line_Fragment; }

			byte chr = *ptr++;
			if (chr == '\r')
			{
				if (ptr == pTmnt) { ptr = ptr_for_rollback; return RET.Detect_Line_Fragment; }
				ptr++;
				break;
			}
			if (chr == '\n') { break; }
		}

		return RET.Consume_1Line;
	}

	// ------------------------------------------------------------------------------------
	// m_idx_consume のところから、コンテンツの終端を探す
	// 戻り値： bool -> コンテンツの終端が見つかったら true、そうでなければ false
	// bytes -> consume すべきバイト数（クライアントに送信すべきバイト数。0 である場合もある -> ReadAsync が必要）
	public unsafe (bool, int bytes) Consume_Content()
	{
		//+++++ エラー顕在化
		if (m_idx_consume >= m_valid_bytes_cur_buf)
		{ throw new Exception("m_idx_consume >= m_valid_bytes_cur_buf の状態で Consume_Content() がコールされました。"); }

		if (m_rem_content_length > 0)
		{
			// content-length を受け取っていた時の処理
			int rem_bytes_buf = m_valid_bytes_cur_buf - m_idx_consume;
			if (m_rem_content_length <= rem_bytes_buf)
			{
				m_idx_consume += m_rem_content_length;
				int bytes_consume = m_rem_content_length;

				// content の取得が終了した場合
				Reset_ContentLengthInfo();
				return (true, bytes_consume);
			}

			// m_rem_content_length > rem_bytes_buf のときの処理
			m_rem_content_length -= rem_bytes_buf;
			m_idx_consume = m_valid_bytes_cur_buf;  // m_idx_consume += rem_bytes_buf に同じ

			return (false, rem_bytes_buf);
		}

		if (mb_enc_gzip == true)
		{
//			KLog_Study.HexDump(m_ary_buf_response, m_idx_consume, 10);

			// chunk の処理をする
			if (m_rem_chunk_cur < 0)
			{
				// 新規にチャンクを consume する
				// まず、チャンクのバイト数を読み出す
				int len = 0;
				fixed (byte* pary_buf = m_ary_buf_response)
				{
					byte* ptr = pary_buf + m_idx_consume;
					byte* ptr_on_start = ptr;
					byte* pTmnt = pary_buf + m_valid_bytes_cur_buf;

					while (true)
					{
						// 終端チャンクブロックかどうかのチェック
						if (pTmnt - ptr < 3)
						{
							// この場合は、終端チャンクブロックかどうかに関わらず、もう読み出せない
							break;
						}

						if ((*(uint*)ptr & 0xff_ffff) == 0x0a_0d_30)  // 0 \r \n
						{
							int bytes_cnsm = (int)(ptr - ptr_on_start + 3);
							m_idx_consume += bytes_cnsm;

							// content の取得が終了した場合
							Reset_ContentLengthInfo();
							return (true, bytes_cnsm);
						}

						// ptr から、チャンクブロックを読み出す
						if (Read_NewChunkBlk(ref ptr, pTmnt) == false) { break; }
					} // while

					// フラグメントのあるチャンクブロックを検出した場合
					// m_rem_chunk_cur の値の設定は既にされている
					int bytes_consume = (int)(ptr - ptr_on_start);
					m_idx_consume += bytes_consume;
					return (false, bytes_consume);
				} // fixed
			}

			// m_rem_chunk_cur >= 0 であるときの処理
		}

		//+++++ エラー顕在化
		throw new Exception("Consume_Content() がコールされましたが、該当する処理が存在しませんでした。");
	}

	// ------------------------------------------------------------------------------------
	// 戻り値： bool -> １つのチャンクブロックを読み取り終えた場合 true、そうでない場合 false。
	// 変更されるメンバ変数 -> m_rem_chunk_cur
	// ptr -> 変更なし / 次のチャンクブロックの先頭 / チャンクブロックボディの終端 + 1 / pTmnnt のいずれか
	// ptr が変更なし、となるのは、bytes_chunk の読み出しができなかったとき。consume bytes = 0 となる。
	unsafe bool Read_NewChunkBlk(ref byte* ptr, byte* pTmnt)
	{
		// チャンクのバイト数の読み出し
		int bytes_chunk = 0;
		byte* ptr_on_called = ptr;

		while (true)
		{
			if (ptr == pTmnt) { ptr = ptr_on_called; return false; }
			int chr = *ptr++;
			if (chr == '\r') { break; }

			if (chr >= 0x40)
			{
				chr |= 0x20;  // 大文字 -> 小文字
				if (chr < 'a' || chr > 'f')
				{ throw new Exception("16進数を検出中に、不正なものを検出しました。"); }

				bytes_chunk = (bytes_chunk << 4) + chr - 87;  // 87 = 0x61('a') - 10
			}
			else if (chr < '0' || chr > '9')
			{
				throw new Exception("16進数を検出中に、不正なものを検出しました。");
			}
			else
			{
				bytes_chunk = (bytes_chunk << 4) + chr - 0x30;
			}
		}
		// ptr は '\n' の部分を指している
		if (ptr == pTmnt) { ptr = ptr_on_called; return false; }

		ptr++;  // ptr をチャンクの先頭に移動

		int rem_bytes_buf = (int)(pTmnt - ptr);
		if (bytes_chunk > rem_bytes_buf)
		{
			// チャンクのフラグメントを検知した場合
			m_rem_chunk_cur = bytes_chunk - rem_bytes_buf;
			ptr = pTmnt;
			return false;
		}

		// チャンクのボディを全て読み出せる場合（bytes_chunk <= rem_bytes_buf のとき）
		ptr += bytes_chunk;

		// \r\n を探す
		byte* ptr_for_rollback = ptr;  // チャンクボディの終端 + 1 のところを指している
		while (true)
		{
			if (ptr == pTmnt)
			{
				m_rem_chunk_cur = 0;
				ptr = ptr_for_rollback;
				return false;
			}

			if (*ptr++ == '\r') { break; }
		}
		// ptr は '\n' のところを指している

		if (ptr == pTmnt)
		{
			m_rem_chunk_cur = 0;
			ptr = ptr_for_rollback;
			return false;
		}

		ptr++;  // ptr は、次のチャンクブロックの先頭を指すことになる
		m_rem_chunk_cur = -1;
		return true;
	}

	// ------------------------------------------------------------------------------------
	public unsafe void ShowToLog(int bytes)
	{
		fixed(byte* pary_buf_response = m_ary_buf_response)
		{
			byte* psrc = pary_buf_response;
			byte* pTmnt_src = psrc + bytes;

			string str;
			while ((str = DBG_Get_1Line(ref psrc, pTmnt_src)) != null)
			{
				KLog.Write(str);
			}
		}
	}

	// ------------------------------------------------------------------------------------
	//「改行を含めて」文字列を取り出す
	// 空行の場合は、null が返される
	unsafe string DBG_Get_1Line(ref byte* ptr, byte* pTmnt)
	{
		byte* ptop = ptr;

		while (true)
		{
			if (ptr >= pTmnt)
			{ throw new Exception("!!! Response_Reader.DBG_Get_1Line() : 読み取りオーバー発生"); }

			byte chr = *ptr;
			if (chr == '\n')
			{
				if (ptr == ptop)
				{
					ptr++;
					return null;
				}
				ptr++;
				break;
			}
			if (chr == '\r')
			{
				if (ptr == ptop)
				{
					ptr += 2;
					return null;
				}
				ptr += 2;
				break;
			}
			ptr++;
		}

		// ptr は '\n' の位置を指している
		return Encoding.UTF8.GetString(ptop, (int)(ptr - ptop));
	}

	// ------------------------------------------------------------------------------------

} // class ResHead_Reader
} // namespace proxy


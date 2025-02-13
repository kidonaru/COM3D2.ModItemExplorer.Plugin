#!/usr/bin/env python3
# -*- coding: utf-8 -*-

import base64
import textwrap
import sys
import os

def convert_to_base64_csharp(file_path):
    # ファイルをバイナリモードで読み込む
    with open(file_path, "rb") as file:
        binary_data = file.read()

    icon_name = os.path.splitext(os.path.basename(file_path))[0]

    # キャメルケースに変換
    icon_name = ''.join([word.capitalize() for word in icon_name.split('_')])

    # バイナリデータをbase64にエンコードし、76文字ごとに改行
    base64_encoded_data = base64.b64encode(binary_data).decode('utf-8')
    wrapped_encoded_data = textwrap.fill(base64_encoded_data, 76)
    
    # C#のコード形式で出力
    print(f'public readonly static byte[] {icon_name} = Convert.FromBase64String(')
    print('        "' + wrapped_encoded_data.replace('\n', '" +\n        "') + '");')

if __name__ == '__main__':
    if len(sys.argv) != 2:
        print("使用方法: python convert_to_base64_csharp.py <ファイルパス>")
    else:
        file_path = sys.argv[1]
        convert_to_base64_csharp(file_path)

// 验证终端页底部快捷操作栏与网页端对齐
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:pm_mobile/pages/terminal_page.dart';
import 'package:pm_mobile/session.dart';

/// 不发真实网络请求的假 Session：只提供 baseUrl/token 用于拼 WS 地址
class _FakeSession extends Session {
  _FakeSession() {
    baseUrl = 'http://127.0.0.1:1'; // 端口不可达，连接会失败但不影响 UI 断言
    token = 'fake-token';
  }
}

void main() {
  testWidgets('终端页底部快捷操作栏元素齐全', (tester) async {
    tester.view.physicalSize = const Size(1080, 2400);
    tester.view.devicePixelRatio = 3.0;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);

    await tester.pumpWidget(
      MaterialApp(home: TerminalPage(session: _FakeSession(), sessionId: 1)),
    );
    await tester.pump(const Duration(milliseconds: 100));

    // 第一行：上传按钮 + 命令输入框 + 发送按钮
    expect(find.byTooltip('上传文件到终端'), findsOneWidget);
    expect(find.text('输入命令，回车发送到终端'), findsOneWidget);
    expect(find.widgetWithText(FilledButton, '发送'), findsOneWidget);

    // 第二行：全部快捷按键
    for (final label in ['↑', '↓', '←', '→', 'Tab', 'ok', '继续', '换行', '空格', '⏎ 回车']) {
      expect(
        find.text(label),
        findsOneWidget,
        reason: '缺少快捷键: $label',
      );
    }

    // 发送按钮初始应禁用（输入框为空）
    final sendBtn = tester.widget<FilledButton>(
      find.widgetWithText(FilledButton, '发送'),
    );
    expect(sendBtn.onPressed, isNull, reason: '空输入时发送按钮应禁用');

    // 输入内容后发送按钮应可用
    await tester.enterText(find.byType(TextField), 'ls -la');
    await tester.pump();
    final sendBtn2 = tester.widget<FilledButton>(
      find.widgetWithText(FilledButton, '发送'),
    );
    expect(sendBtn2.onPressed, isNotNull, reason: '有输入时发送按钮应可用');
  });
}

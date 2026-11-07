// 验证登录页确实渲染出服务器地址输入框，且交互可用
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:pm_mobile/pages/login_page.dart';
import 'package:pm_mobile/session.dart';

void main() {
  testWidgets('登录页包含服务器地址输入框', (tester) async {
    // 手机竖屏视口，避免内容被测试默认 800x600 截断
    tester.view.physicalSize = const Size(1080, 2400);
    tester.view.devicePixelRatio = 3.0;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);

    final session = Session();
    await tester.pumpWidget(
      MaterialApp(home: LoginPage(session: session)),
    );
    await tester.pumpAndSettle();

    // 1. 地址输入框存在
    expect(find.widgetWithText(TextFormField, '服务器地址'), findsOneWidget);
    // 2. 账号、密码也在
    expect(find.widgetWithText(TextFormField, '账号'), findsOneWidget);
    expect(find.widgetWithText(TextFormField, '密码'), findsOneWidget);
    // 3. 快捷地址 chip
    expect(find.widgetWithText(ActionChip, '192.168.2.110:8080'), findsOneWidget);
    // 4. 测试连接按钮
    expect(find.byTooltip('测试连接'), findsOneWidget);
    // 5. 登录按钮
    expect(find.widgetWithText(FilledButton, '登 录'), findsOneWidget);

    // 6. 能输入地址
    await tester.enterText(
      find.widgetWithText(TextFormField, '服务器地址'),
      '192.168.10.199:8080',
    );
    await tester.pump();
    final edt = tester.widget<EditableText>(find.byType(EditableText).first);
    expect(edt.controller.text, '192.168.10.199:8080');

    // 7. 地址为空时点登录应报错
    await tester.enterText(find.widgetWithText(TextFormField, '服务器地址'), '');
    await tester.pump();
    await tester.tap(find.widgetWithText(FilledButton, '登 录'));
    await tester.pumpAndSettle();
    expect(find.text('请输入服务器地址'), findsOneWidget);
  });
}

import XCTest

final class MobileFlowTests: XCTestCase {
    override func setUpWithError() throws { continueAfterFailure = false }
    func testKeyboardAndHologramRoundTrip() {
        let app = XCUIApplication(); app.launchArguments = ["--hologram-preview", "ai-keyboard", "--qa-keyboard"]; app.launch()
        let field = app.descendants(matching: .any)["ai.composer"].firstMatch
        XCTAssertTrue(field.waitForExistence(timeout: 10))
        let tutorial = app.buttons["Continue"]
        if tutorial.waitForExistence(timeout: 2) { tutorial.tap() }
        XCTAssertTrue(app.keyboards.firstMatch.waitForExistence(timeout: 5))
        field.tap(); field.typeText("Check project tasks")
        XCTAssertTrue(field.isHittable)
        XCTAssertTrue(app.buttons["发送问题"].exists)
        XCTAssertLessThanOrEqual(field.frame.maxY, app.keyboards.firstMatch.frame.minY + 1)
        XCTAssertLessThanOrEqual(app.buttons["发送问题"].frame.maxY, app.keyboards.firstMatch.frame.minY + 1)
        let shot = XCTAttachment(screenshot: app.screenshot()); shot.name = "AI keyboard and send controls"; shot.lifetime = .keepAlways; add(shot)
        app.buttons["ai.hologram"].tap()
        XCTAssertTrue(app.buttons["退出全息通话"].waitForExistence(timeout: 5))
        app.buttons["退出全息通话"].tap()
        XCTAssertTrue(app.buttons["ai.hologram"].waitForExistence(timeout: 5))
    }
    func testToolCardsCollapseAndExpand() {
        let app = XCUIApplication(); app.launchArguments = ["--hologram-preview", "tool-history"]; app.launch()
        let card = app.buttons["ai.tool.toggle.deskbox_tasks"]
        let raw = app.descendants(matching: .any)["ai.tool.raw"].firstMatch
        XCTAssertTrue(card.waitForExistence(timeout: 10))
        XCTAssertTrue((card.value as? String)?.contains("已收起") == true)
        XCTAssertFalse(raw.exists)
        XCTAssertFalse(app.staticTexts.containing(NSPredicate(format: "label CONTAINS %@", "• 超滤SIP")).firstMatch.exists)
        let collapsed = XCTAttachment(screenshot: app.screenshot()); collapsed.name = "Tool result collapsed"; collapsed.lifetime = .keepAlways; add(collapsed)
        card.tap()
        XCTAssertTrue(app.staticTexts.containing(NSPredicate(format: "label CONTAINS %@", "• 超滤SIP")).firstMatch.waitForExistence(timeout: 5))
        XCTAssertFalse(raw.exists)
        let expanded = XCTAttachment(screenshot: app.screenshot()); expanded.name = "Readable tool result expanded"; expanded.lifetime = .keepAlways; add(expanded)
        app.buttons["ai.tool.raw-toggle.deskbox_tasks"].tap()
        XCTAssertTrue(raw.waitForExistence(timeout: 5))
        app.buttons["ai.tool.raw-toggle.deskbox_tasks"].tap()
        XCTAssertFalse(raw.exists)
        card.tap()
        XCTAssertTrue((card.value as? String)?.contains("已收起") == true)
        XCTAssertTrue(app.staticTexts.containing(NSPredicate(format: "label CONTAINS %@", "• 超滤SIP")).firstMatch.waitForNonExistence(timeout: 5))
    }
    func testAttachmentPreviewAndRemove() {
        let app = XCUIApplication(); app.launchArguments = ["--hologram-preview", "ai-attachment"]; app.launch()
        let preview = app.buttons["ai.attachment.preview.qa-attachment"]
        XCTAssertTrue(preview.waitForExistence(timeout: 10))
        XCTAssertTrue(app.buttons["ai.attach"].isHittable)
        preview.tap()
        XCTAssertTrue(app.descendants(matching: .any)["ai.attachment.text.qa-attachment"].waitForExistence(timeout: 5))
        preview.tap()
        XCTAssertTrue(app.descendants(matching: .any)["ai.attachment.text.qa-attachment"].waitForNonExistence(timeout: 5))
        let composer = app.descendants(matching: .any)["ai.composer"].firstMatch
        composer.tap()
        let tutorial = app.buttons["Continue"]
        if tutorial.waitForExistence(timeout: 2) { tutorial.tap() }
        XCTAssertTrue(app.keyboards.firstMatch.waitForExistence(timeout: 5))
        composer.typeText("Review attachment")
        XCTAssertTrue(app.descendants(matching: .any)["ai.attachment.compact"].firstMatch.exists)
        XCTAssertLessThanOrEqual(app.buttons["发送问题"].frame.maxY, app.keyboards.firstMatch.frame.minY + 1)
        let screenshot = XCTAttachment(screenshot: app.screenshot()); screenshot.name = "Attachment preview and remove"; screenshot.lifetime = .keepAlways; add(screenshot)
        app.buttons["ai.attachment.remove"].tap()
        XCTAssertTrue(app.descendants(matching: .any)["ai.attachment.compact"].firstMatch.waitForNonExistence(timeout: 5))
        XCTAssertTrue(app.descendants(matching: .any)["ai.composer"].isHittable)
    }
    func testWorkspaceTabsAndTaskSave() {
        let app = XCUIApplication(); app.launchArguments = ["--hologram-preview", "workspace"]; app.launch()
        XCTAssertTrue(app.tabBars.buttons["任务"].waitForExistence(timeout: 10)); app.tabBars.buttons["任务"].tap()
        app.buttons["新建任务"].tap()
        let title = app.descendants(matching: .any)["task.title"].firstMatch
        XCTAssertTrue(title.waitForExistence(timeout: 5)); title.tap(); title.typeText("QA offline task")
        app.buttons["保存"].tap()
        XCTAssertTrue(app.staticTexts["QA offline task"].waitForExistence(timeout: 5))
        for name in ["项目", "事件", "AI", "首页"] { app.tabBars.buttons[name].tap(); XCTAssertTrue(app.tabBars.buttons[name].isSelected) }
    }
}

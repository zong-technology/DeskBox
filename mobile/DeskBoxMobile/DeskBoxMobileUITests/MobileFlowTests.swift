import XCTest

final class MobileFlowTests: XCTestCase {
    override func setUpWithError() throws { continueAfterFailure = false }
    func testKeyboardAndHologramRoundTrip() {
        let app = XCUIApplication(); app.launchArguments = ["--hologram-preview", "ai-keyboard", "--qa-keyboard"]; app.launch()
        let field = app.textFields["ai.composer"].exists ? app.textFields["ai.composer"] : app.textViews["ai.composer"]
        XCTAssertTrue(field.waitForExistence(timeout: 10))
        let tutorial = app.buttons["Continue"]
        if tutorial.waitForExistence(timeout: 2) { tutorial.tap() }
        XCTAssertTrue(app.keyboards.firstMatch.waitForExistence(timeout: 5))
        field.tap(); field.typeText("Check project tasks")
        XCTAssertTrue(field.isHittable)
        XCTAssertTrue(app.buttons["发送问题"].isHittable)
        let shot = XCTAttachment(screenshot: app.screenshot()); shot.name = "AI keyboard and send controls"; shot.lifetime = .keepAlways; add(shot)
        app.buttons["ai.hologram"].tap()
        XCTAssertTrue(app.buttons["退出全息通话"].waitForExistence(timeout: 5))
        app.buttons["退出全息通话"].tap()
        XCTAssertTrue(app.buttons["ai.hologram"].waitForExistence(timeout: 5))
    }
    func testWorkspaceTabsAndTaskSave() {
        let app = XCUIApplication(); app.launchArguments = ["--hologram-preview", "workspace"]; app.launch()
        XCTAssertTrue(app.tabBars.buttons["任务"].waitForExistence(timeout: 10)); app.tabBars.buttons["任务"].tap()
        app.buttons["新建任务"].tap()
        let title = app.textFields["任务标题"].exists ? app.textFields["任务标题"] : app.textViews["任务标题"]
        XCTAssertTrue(title.waitForExistence(timeout: 5)); title.tap(); title.typeText("QA offline task")
        app.buttons["保存"].tap()
        XCTAssertTrue(app.staticTexts["QA offline task"].waitForExistence(timeout: 5))
        for name in ["项目", "事件", "AI", "首页"] { app.tabBars.buttons[name].tap(); XCTAssertTrue(app.tabBars.buttons[name].isSelected) }
    }
}

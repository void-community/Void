package voidclient.agent;

import java.lang.ref.WeakReference;
import java.lang.reflect.Field;
import java.lang.reflect.Method;
import java.util.Collections;
import java.util.Map;
import org.junit.After;
import org.junit.Assert;
import org.junit.Before;
import org.junit.Test;

public final class MenuReadinessTest {
    private static final String ScreenBase = Screen.class.getName().replace('.', '/');

    @Before
    @After
    public void reset() throws Exception {
        field("menuScreenName").set(null, null);
        field("menuFailure").set(null, null);
        field("following").set(null, false);
        field("screenSetterReceiver").set(null, new WeakReference<Object>(null));
        ((Map<?, ?>) field("IndexFailures").get(null)).clear();
        ((Map<?, ?>) field("LoaderIndexes").get(null)).clear();
    }

    @Test
    public void gameIsReadyOnceItShowsAMenuWithoutAnOverlay() throws Exception {
        Client client = new Client();
        field("screenSetterReceiver").set(null, new WeakReference<Object>(client));
        GameAutomationIndex.IndexedCode index = index("overlay");

        observe(index);
        Assert.assertTrue(GameAutomationController.readyJson().contains("\"menu.loading\""));

        client.screen = new TitleScreen();
        client.overlay = new Object();
        observe(index);
        Assert.assertTrue(GameAutomationController.readyJson().contains("\"menu.loading\""));

        client.overlay = null;
        observe(index);
        Assert.assertEquals("{\"status\":\"ok\",\"stage\":\"menu.ready\",\"value\":\"" + TitleScreen.class.getName() + "\"}", GameAutomationController.readyJson());

        // The menu stays seen, whatever the game shows later.
        client.screen = null;
        observe(index);
        Assert.assertTrue(GameAutomationController.readyJson().contains("\"menu.ready\""));
    }

    @Test
    public void menuThatCannotBeReadIsReportedOnce() throws Exception {
        field("screenSetterReceiver").set(null, new WeakReference<Object>(new Client()));

        observe(index("missing"));

        String ready = GameAutomationController.readyJson();
        Assert.assertTrue(ready, ready.contains("\"code.discovery\"") && ready.contains("Reading the game's menu failed"));
    }

    @Test
    public void gameTheAgentCannotFollowIsReportedInsteadOfLoadingForever() throws Exception {
        for (String location : new String[] { GameAutomationController.GameHooksFailure, GameAutomationController.ClientFrameFailure }) {
            reset();
            GameAutomationController.recordIndexFailure(location, "synthetic failure");

            String ready = GameAutomationController.readyJson();
            Assert.assertTrue(ready, ready.contains("\"code.discovery\"") && ready.contains("synthetic failure"));
        }
    }

    @Test
    public void gameCodeThatFailsToIndexMattersUntilTheAgentFollowsTheGame() throws Exception {
        // Issue #1622: on Forge 1.20.1 the obfuscated Minecraft jar indexed for another class loader, which hid the game's own failure.
        GameAutomationController.recordIndexFailure(GameAutomationController.GameCodeFailure, "No unique chat screen submission flow was found");
        @SuppressWarnings("unchecked")
        Map<ClassLoader, GameAutomationIndex.IndexedCode> indexes = (Map<ClassLoader, GameAutomationIndex.IndexedCode>) field("LoaderIndexes").get(null);
        indexes.put(MenuReadinessTest.class.getClassLoader(), index("overlay"));

        String ready = GameAutomationController.readyJson();
        Assert.assertTrue(ready, ready.contains("\"code.discovery\"") && ready.contains("No unique chat screen"));

        field("following").set(null, true);

        Assert.assertTrue(GameAutomationController.readyJson().contains("\"menu.loading\""));
    }

    @Test(timeout = 5000)
    public void connectToAGameTheAgentCannotFollowFailsAtOnce() throws Exception {
        // Issue #1622: such a connect waited forever, because nothing ever completed it.
        GameAutomationController.recordIndexFailure(GameAutomationController.GameCodeFailure, "No unique chat screen submission flow was found");

        String response = GameAutomationController.connectJson("request", "server:25565");

        Assert.assertTrue(response, response.contains("\"code.discovery\"") && response.contains("No unique chat screen"));
    }

    private static void observe(GameAutomationIndex.IndexedCode index) throws Exception {
        Method observeMenu = GameAutomationController.class.getDeclaredMethod("observeMenu", GameAutomationIndex.IndexedCode.class);
        observeMenu.setAccessible(true);
        observeMenu.invoke(null, index);
    }

    private static GameAutomationIndex.IndexedCode index(String overlayField) {
        PresentationOverlayPlan overlay = new PresentationOverlayPlan(Client.class.getName().replace('.', '/'), overlayField, "Ljava/lang/Object;");
        AutomationPlan plan = new AutomationPlan(ScreenBase, Client.class.getName().replace('.', '/'), "set", ScreenBase, null, null, null, overlay, null,
            null, null, Collections.<String[]>emptyList());

        return new GameAutomationIndex.IndexedCode(plan, Collections.<String, String>emptyMap());
    }

    private static Field field(String name) throws Exception {
        Field field = GameAutomationController.class.getDeclaredField(name);
        field.setAccessible(true);
        return field;
    }

    public static class Screen {
    }

    public static class TitleScreen extends Screen {
    }

    public static class Client {
        public Screen screen;
        public Object overlay;
    }
}

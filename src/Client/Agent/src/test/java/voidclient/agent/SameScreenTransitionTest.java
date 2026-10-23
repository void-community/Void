package voidclient.agent;

import java.lang.ref.WeakReference;
import java.lang.reflect.Constructor;
import java.lang.reflect.Field;
import java.lang.reflect.Method;
import java.util.Collections;
import java.util.List;
import java.util.Map;
import org.junit.After;
import org.junit.Assert;
import org.junit.Before;
import org.junit.Test;

public final class SameScreenTransitionTest {
    private static final String Prefix = "voidclient/agent/SameScreenTransitionTest$";
    private static final Client Game = new Client();

    @Before
    @After
    public void reset() throws Exception {
        field("screenSetterReceiver").set(null, new WeakReference<Object>(null));
        field("consumedScreen").set(null, null);
        field("waitingScreenName").set(null, null);
    }

    @Test
    public void transitionThatLeavesTheSameScreenOpenHasNoRoute() throws Exception {
        // Issue #1616: Minecraft 1.21.1's accessibility onboarding screen sets the screen it is given, which the agent could only fill
        // with the onboarding screen itself, so the connect counted that as progress and waited forever.
        Assert.assertNotEquals(0L, connectFrom("forward"));
        Assert.assertEquals(0L, connectFrom("leave"));
    }

    private static long connectFrom(String methodName) throws Exception {
        Onboarding screen = new Onboarding();
        Game.screen = screen;
        field("screenSetterReceiver").set(null, new WeakReference<Object>(Game));
        TransitionPlan transition = new TransitionPlan(Prefix + "Onboarding", methodName, "(L" + Prefix + "Screen;)V", false, Collections.<String>emptyList(),
            true, false, null, null, null, false, new boolean[] { true });
        Map<String, List<TransitionPlan>> transitions = Collections.singletonMap(Prefix + "Onboarding", Collections.singletonList(transition));
        AutomationPlan plan = new AutomationPlan(Prefix + "Screen", Prefix + "Client", "set", Prefix + "Direct", null, null, null, null, null, null,
            transitions, Collections.<String[]>emptyList());
        GameAutomationIndex.IndexedCode index = new GameAutomationIndex.IndexedCode(plan, Collections.singletonMap(Prefix + "Onboarding", Prefix + "Screen"));
        Class<?> operationType = Class.forName(GameAutomationController.class.getName() + "$PendingOperation");
        Constructor<?> constructor = operationType.getDeclaredConstructor(String.class, String.class, String.class);
        constructor.setAccessible(true);
        Object operation = constructor.newInstance("request", "connect", "server:25565");
        Method applyConnect = GameAutomationController.class.getDeclaredMethod("applyConnect", Object.class, GameAutomationIndex.IndexedCode.class, operationType);
        applyConnect.setAccessible(true);

        applyConnect.invoke(null, screen, index, operation);

        Field unroutableSince = operationType.getDeclaredField("unroutableSince");
        unroutableSince.setAccessible(true);
        return unroutableSince.getLong(operation);
    }

    private static Field field(String name) throws Exception {
        Field field = GameAutomationController.class.getDeclaredField(name);
        field.setAccessible(true);
        return field;
    }

    public static class Screen {
    }

    public static class Title extends Screen {
    }

    public static class Onboarding extends Screen {
        public void forward(Screen screen) {
            Game.screen = screen;
        }

        public void leave(Screen screen) {
            Game.screen = new Title();
        }
    }

    public static class Client {
        public Screen screen;
    }
}

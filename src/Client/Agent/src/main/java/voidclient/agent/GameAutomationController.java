package voidclient.agent;

import java.lang.ref.WeakReference;
import java.lang.reflect.Array;
import java.lang.reflect.Constructor;
import java.lang.reflect.Field;
import java.lang.reflect.Method;
import java.lang.reflect.Modifier;
import java.lang.instrument.Instrumentation;
import java.util.ArrayList;
import java.util.Collection;
import java.util.Collections;
import java.util.HashMap;
import java.util.HashSet;
import java.util.IdentityHashMap;
import java.util.List;
import java.util.Map;
import java.util.Set;
import java.util.concurrent.CountDownLatch;
import org.objectweb.asm.Type;

public final class GameAutomationController {
    /** Where failures are recorded that leave the agent unable to follow the game's screens. */
    static final String GameCodeFailure = "game-code";
    static final String GameHooksFailure = "game hooks";
    static final String ClientFrameFailure = "client frame";
    private static final Map<ClassLoader, GameAutomationIndex.IndexedCode> LoaderIndexes = Collections.synchronizedMap(new java.util.WeakHashMap<ClassLoader, GameAutomationIndex.IndexedCode>());
    private static final Map<String, String> IndexFailures = new HashMap<String, String>();
    private static final Set<String> CancelledRequests = new HashSet<String>();
    private static final long IndexWaitMilliseconds = 600000L;
    private static final long DisconnectReasonWaitMilliseconds = 2000L;
    private static final long RouteGraceMilliseconds = 5000L;
    private static final long RouteDeadlineMilliseconds = 60000L;
    private static final long WorldDeadlineMilliseconds = 300000L;
    private static final ThreadLocal<Boolean> Applying = new ThreadLocal<Boolean>();
    private static volatile PendingOperation pending;
    private static WeakReference<Object> screenSetterReceiver = new WeakReference<Object>(null);
    private static volatile Object consumedScreen;
    private static volatile String menuScreenName;
    private static volatile String menuFailure;
    private static String waitingScreenName;
    private static boolean waitingForPresentationOverlay;
    private static Instrumentation instrumentation;

    private GameAutomationController() {
    }

    static void initialize(Instrumentation value) {
        instrumentation = value;
    }

    static void registerIndex(ClassLoader loader, GameAutomationIndex.IndexedCode index) {
        if (index == null)
            return;

        if (LoaderIndexes.put(loader, index) == index)
            return;

        System.err.println("Void client agent indexed UI graph: direct=" + index.plan.directConnect.describe()
            + " chat=" + index.plan.chat.describe() + " overlay="
            + (index.plan.presentationOverlay == null ? "none" : index.plan.presentationOverlay.describe())
            + " frame=" + index.plan.frame + " screens=" + index.plan.transitions.size());
        retransformClientClass(index.plan.frame.owner);
    }

    private static void retransformClientClass(final String className) {
        final Instrumentation currentInstrumentation = instrumentation;

        if (currentInstrumentation == null)
            return;

        Thread thread = new Thread(new Runnable() {
            @Override
            public void run() {
                for (Class<?> type : currentInstrumentation.getAllLoadedClasses()) {
                    if (!className.equals(type.getName().replace('.', '/')) || !currentInstrumentation.isModifiableClass(type))
                        continue;

                    try {
                        currentInstrumentation.retransformClasses(type);
                    } catch (Throwable exception) {
                        recordIndexFailure(ClientFrameFailure, "Client-thread retransformation of " + className + " failed: " + exception.getMessage());
                    }

                    return;
                }
            }
        }, "Void client agent client-thread instrumentation");
        thread.setDaemon(true);
        thread.start();
    }

    static synchronized void recordIndexFailure(String location, String message) {
        IndexFailures.put(location, message);
        System.err.println("Void client agent UI index failed for " + location + ": " + message);
    }

    static String connectJson(String requestId, String address) {
        return await(new PendingOperation(requestId, "connect", address));
    }

    static String chatJson(String requestId, String message) {
        return await(new PendingOperation(requestId, "chat", message));
    }

    static synchronized void cancel(String requestId) {
        if (pending != null && pending.requestId.equals(requestId)) {
            complete(pending, error("request.cancelled", "The agent operation was cancelled"));
            pending = null;
        } else {
            CancelledRequests.add(requestId);
        }
    }

    private static String await(PendingOperation operation) {
        synchronized (GameAutomationController.class) {
            if (pending != null)
                return error("request.pending", "Another game automation operation is already pending");

            // NeoForge shows its loading window before Minecraft's UI classes load, so wait for them to be indexed.
            long deadline = System.currentTimeMillis() + IndexWaitMilliseconds;

            try {
                while (LoaderIndexes.isEmpty() && !CancelledRequests.contains(operation.requestId) && System.currentTimeMillis() < deadline)
                    GameAutomationController.class.wait(500L);
            } catch (InterruptedException exception) {
                Thread.currentThread().interrupt();
                return error("request.interrupted", "The agent operation was interrupted");
            }

            if (CancelledRequests.remove(operation.requestId))
                return error("request.cancelled", "The agent operation was cancelled");

            if (pending != null)
                return error("request.pending", "Another game automation operation is already pending");

            if (LoaderIndexes.isEmpty())
                return error("code.discovery", "Minecraft UI bytecode has not been indexed; failures=" + IndexFailures);

            pending = operation;
            consumedScreen = null;
            waitingForPresentationOverlay = false;
        }

        try {
            operation.completed.await();
            return operation.response;
        } catch (InterruptedException exception) {
            Thread.currentThread().interrupt();
            cancel(operation.requestId);
            return error("request.interrupted", "The agent operation was interrupted");
        }
    }

    public static void applyScreen(Object screen) {
        if (screen == null || consumedScreen == screen || Boolean.TRUE.equals(Applying.get()))
            return;

        PendingOperation operation;
        GameAutomationIndex.IndexedCode index = index(screen.getClass().getClassLoader());

        synchronized (GameAutomationController.class) {
            if (index != null && screenSetterReceiver.get() == null)
                screenSetterReceiver = new WeakReference<Object>(findObject(screen, index.plan.clientClassName, 3));

            operation = pending;
        }

        if (operation == null || index == null || consumedScreen == screen)
            return;

        if (!isCurrentScreen(screenSetterReceiver.get(), screen, index.plan.screenBaseName))
            return;

        Applying.set(Boolean.TRUE);

        try {
            if ("connect".equals(operation.kind))
                applyConnect(screen, index, operation);
        } catch (Throwable exception) {
            fail(operation, "ui.action", exception, index, screen);
        } finally {
            Applying.remove();
        }
    }

    public static void applyClient(Object client) {
        try {
            advanceClient(client);
        } catch (Throwable exception) {
            PendingOperation operation = pending;
            GameAutomationIndex.IndexedCode index = index(client.getClass().getClassLoader());
            if (operation != null && index != null)
                fail(operation, "ui.lifecycle", exception, index, null);
            else
                exception.printStackTrace(System.err);
        }
    }

    private static void advanceClient(Object client) {
        if (Boolean.TRUE.equals(Applying.get()))
            return;

        PendingOperation operation;
        GameAutomationIndex.IndexedCode index = index(client.getClass().getClassLoader());

        if (index == null)
            return;

        synchronized (GameAutomationController.class) {
            screenSetterReceiver = new WeakReference<Object>(findObject(client, index.plan.clientClassName, 1));
            Tracker.bindClient(client);

            operation = pending;
        }

        observeMenu(index);

        if (operation == null)
            return;

        try {
            if (isPresentationOverlayActive(screenSetterReceiver.get(), index.plan.presentationOverlay)) {
                synchronized (GameAutomationController.class) {
                    if (!waitingForPresentationOverlay) {
                        waitingForPresentationOverlay = true;
                        System.err.println("Void client agent is waiting for the active presentation overlay to clear");
                    }
                }

                return;
            }

            synchronized (GameAutomationController.class) {
                if (waitingForPresentationOverlay) {
                    waitingForPresentationOverlay = false;
                    System.err.println("Void client agent observed the presentation overlay clear");
                }
            }
        } catch (Throwable exception) {
            fail(operation, "ui.readiness", exception, index, null);
            return;
        }

        if ("connect".equals(operation.kind)) {
            if (operation.submitted) {
                boolean closed = Boolean.FALSE.equals(JoinedConnection.isOpen(operation.connection));

                if (operation.rejection != null)
                    complete(operation, error("connection.rejected", operation.rejection));
                else if (closed)
                    awaitDisconnectReason(operation);
                else if (Tracker.activePlayer(client) != null) {
                    JoinedConnection.joined(operation.connection);
                    complete(operation, "{\"status\":\"ok\",\"stage\":\"world.ready\",\"value\":" + quote(operation.value) + "}");
                }
                else if (Tracker.describeUnexpectedPlayer(client) != null)
                    complete(operation, error("player.unexpected", Tracker.describeUnexpectedPlayer(client)));
                else
                    checkJoin(index, operation);
                return;
            }
            Object screen = findCurrentScreen(screenSetterReceiver.get(), index.plan.screenBaseName);
            Set<Object> visited = Collections.newSetFromMap(new IdentityHashMap<Object, Boolean>());
            while (screen != null && visited.add(screen) && pending == operation && !operation.submitted) {
                applyScreen(screen);
                Object nextScreen = findCurrentScreen(screenSetterReceiver.get(), index.plan.screenBaseName);
                if (nextScreen == screen)
                    break;
                screen = nextScreen;
            }
            if (pending == operation && !operation.submitted) {
                try {
                    checkRoute(index, operation, client.getClass().getClassLoader());
                } catch (Throwable exception) {
                    fail(operation, "ui.route", exception, index, findCurrentScreen(screenSetterReceiver.get(), index.plan.screenBaseName));
                }
            }
            return;
        }

        if (!"chat".equals(operation.kind))
            return;

        Applying.set(Boolean.TRUE);
        Object previousScreen = findCurrentScreen(screenSetterReceiver.get(), index.plan.screenBaseName);

        try {
            // The chat screen builds its suggestions from the player's connection, so it can't open once the game has left the server.
            String closedReason = JoinedConnection.closedReason();

            if (closedReason != null) {
                complete(operation, disconnected("connection.closed", "The game left the server: " + closedReason));
                return;
            }

            if (Tracker.activePlayer(client) == null) {
                complete(operation, disconnected("world.absent", "The game has no player in a world to chat from"));
                return;
            }

            Object chatScreen = constructChatScreen(index.plan.chat, client.getClass().getClassLoader());
            setScreen(index.plan, chatScreen);
            submitChat(chatScreen, index.plan.chat, operation.value);
            setScreen(index.plan, null);

            complete(operation, "{\"status\":\"ok\",\"stage\":\"chat.submit\",\"value\":" + quote(operation.value) + "}");
        } catch (Throwable exception) {
            // Minecraft makes a screen current before setting it up, so a chat screen whose setup threw would crash the next frame.
            restoreScreen(index.plan, previousScreen);
            fail(operation, "chat.submit", exception, index, null);
        } finally {
            Applying.remove();
        }
    }

    /**
     * Tells whether the game has shown a menu, so it's ready to be used, or why the agent can't tell because it can't follow the game.
     */
    static String readyJson() {
        String screenName = menuScreenName;

        if (screenName != null)
            return "{\"status\":\"ok\",\"stage\":\"menu.ready\",\"value\":" + quote(screenName) + "}";

        String unavailable = automationUnavailableReason();

        return unavailable == null ? "{\"status\":\"loading\",\"stage\":\"menu.loading\"}" : error("code.discovery", unavailable);
    }

    /**
     * Remembers the first frame that shows a menu: a current screen without a presentation overlay over it, such as the title screen,
     * or a notice a mod shows before it. Before then, Minecraft or its loader is still loading.
     */
    private static void observeMenu(GameAutomationIndex.IndexedCode index) {
        if (menuScreenName != null || menuFailure != null)
            return;

        try {
            Object client = screenSetterReceiver.get();

            if (isPresentationOverlayActive(client, index.plan.presentationOverlay))
                return;

            Object screen = findCurrentScreen(client, index.plan.screenBaseName);

            if (screen == null)
                return;

            menuScreenName = screen.getClass().getName();
            System.err.println("Void client agent observed the game menu: " + menuScreenName);
        } catch (Throwable exception) {
            menuFailure = "Reading the game's menu failed: " + exception;
            System.err.println("Void client agent " + menuFailure);
        }
    }

    private static synchronized String automationUnavailableReason() {
        if (menuFailure != null)
            return menuFailure;

        for (String location : new String[] { GameHooksFailure, ClientFrameFailure })
            if (IndexFailures.containsKey(location))
                return "The client can't follow the game's screens: " + IndexFailures.get(location);

        // Mods can have screens that fail to index too, which only matters while the game itself has no index.
        return LoaderIndexes.isEmpty() && IndexFailures.containsKey(GameCodeFailure)
            ? "The client can't follow the game's screens: " + IndexFailures.get(GameCodeFailure)
            : null;
    }

    static String connectionJson() {
        if (!JoinedConnection.hasJoined())
            return error("connection.unknown", "The game has not joined a server");

        String closedReason = JoinedConnection.closedReason();

        return closedReason == null
            ? "{\"status\":\"ok\",\"stage\":\"connection.open\"}"
            : disconnected("connection.closed", closedReason);
    }

    public static synchronized void connectionListenerCreated(Object listener, Object connection) {
        if (pending == null || !pending.submitted || pending.connection != null)
            return;
        pending.connection = connection;
    }

    public static synchronized void connectionRejected(Object listener, Object reason) {
        boolean connecting = pending != null && pending.submitted && pending.connection != null;

        if (!connecting && !JoinedConnection.hasJoined())
            return;

        Object connection = nativeConnection(listener);
        JoinedConnection.disconnected(connection, reason);

        if (connecting && pending.connection == connection)
            pending.rejection = JoinedConnection.reasonText(reason);
    }

    /**
     * Waits for the disconnect callback after the connection closed. The game handles a closed connection on its next tick, so a
     * player can still be in the world on the frame after a server dropped it, and the callback brings the reason.
     */
    private static void awaitDisconnectReason(PendingOperation operation) {
        long now = System.currentTimeMillis();

        if (operation.closedAt == 0L)
            operation.closedAt = now;
        else if (now - operation.closedAt > DisconnectReasonWaitMilliseconds)
            complete(operation, error("connection.rejected", "The connection to the server closed"));
    }

    private static void restoreScreen(AutomationPlan plan, Object screen) {
        if (isCurrentScreen(screenSetterReceiver.get(), screen, plan.screenBaseName))
            return;

        try {
            setScreen(plan, screen);
        } catch (Throwable exception) {
            System.err.println("Void client agent could not restore the screen after a failed automation step: " + exception);
        }
    }

    private static Object nativeConnection(Object listener) {
        for (Class<?> type = listener.getClass(); type != null; type = type.getSuperclass())
            for (Field field : type.getDeclaredFields()) {
                if (Modifier.isStatic(field.getModifiers()))
                    continue;
                for (Class<?> parent = field.getType(); parent != null; parent = parent.getSuperclass()) {
                    if (!("." + parent.getName()).endsWith(".io.netty.channel.SimpleChannelInboundHandler")
                        && !("." + parent.getName()).endsWith(".io.netty.channel.ChannelInboundHandlerAdapter"))
                        continue;
                    try {
                        field.setAccessible(true);
                        return field.get(listener);
                    } catch (IllegalAccessException exception) {
                        recordIndexFailure(type.getName(), exception.toString());
                    }
                }
            }
        return null;
    }

    private static void applyConnect(Object screen, GameAutomationIndex.IndexedCode index, PendingOperation operation) throws Exception {
        String className = screen.getClass().getName().replace('.', '/');

        if (index.plan.directScreenName.equals(className)) {
            operation.submitted = true;
            operation.submittedAt = System.currentTimeMillis();
            String exact = DirectConnectAccess.connect(screen, index.plan.directConnect, operation.value);

            if (!operation.value.equals(exact)) {
                complete(operation, error("address.verify", "Minecraft returned " + quote(exact) + " instead of " + quote(operation.value)));
                return;
            }

            consumedScreen = screen;
            armConnectingScreen(index, operation);
            return;
        }

        if (operation.submitted) {
            consumedScreen = screen;
            return;
        }

        TransitionPlan transition = selectTransition(index, transitions(index, screen.getClass()));

        // Screens outside the UI graph, such as warnings mods show before the title screen, are left through their Proceed button.
        if (transition == null && ProceedButtons.press(screen, index.plan.proceedLabels) != null)
        {
            waitingScreenName = null;
            operation.unroutableSince = 0L;
            System.err.println("Void client warning: Screen " + screen.getClass().getName() + " answered with Proceed");
            consumedScreen = screen;
            return;
        }

        if (transition == null)
        {
            consumedScreen = screen;
            awaitRoute(operation, className);
            return;
        }

        System.err.println("Void client agent invoking UI transition " + transition.describe());
        consumedScreen = screen;

        // A mod menu can leave out the button an older route clicks, which counts as having no route.
        if (invokeTransition(screen, transition)) {
            waitingScreenName = null;
            operation.unroutableSince = 0L;
        } else {
            awaitRoute(operation, className);
        }
    }

    /**
     * Ends a submitted connect that can no longer join. A connection attempt that fails, such as for an unknown host or a refused
     * connection, never creates a connection listener, and the game replaces its connecting screen with one that shows the error. A
     * join that keeps its connection but never reaches a world, behind a screen nobody answers, ends at the deadline.
     */
    private static void checkJoin(GameAutomationIndex.IndexedCode index, PendingOperation operation) {
        armConnectingScreen(index, operation);
        Object screen = findCurrentScreen(screenSetterReceiver.get(), index.plan.screenBaseName);

        if (operation.connection == null && operation.connectingScreen != null && screen != operation.connectingScreen)
            complete(operation, error("connection.failed", screenText(screen, index.plan.screenBaseName)));
        else if (System.currentTimeMillis() - operation.submittedAt >= WorldDeadlineMilliseconds)
            complete(operation, error("world.timeout", "No world " + WorldDeadlineMilliseconds / 60000L
                + " minutes after connecting; the game shows " + screenText(screen, index.plan.screenBaseName)));
    }

    /**
     * Remembers the first screen after submit that holds the game's connection, which is the screen showing the connection attempt.
     * A mod can show other screens before it, and a connection attempt that fails within moments of submit can replace it before
     * it's seen; the connect then waits for the deadline instead of failing a join that may still succeed.
     */
    private static void armConnectingScreen(GameAutomationIndex.IndexedCode index, PendingOperation operation) {
        if (operation.connectingScreen != null)
            return;

        Object screen = findCurrentScreen(screenSetterReceiver.get(), index.plan.screenBaseName);

        if (screen != null && holdsConnection(screen, index.plan.screenBaseName, index.plan.networkTypes))
            operation.connectingScreen = screen;
    }

    static boolean holdsConnection(Object screen, String screenBaseName, Set<String> networkTypes) {
        for (Class<?> type = screen.getClass(); type != null && !screenBaseName.equals(type.getName().replace('.', '/')); type = type.getSuperclass())
            for (Field field : type.getDeclaredFields())
                if (!Modifier.isStatic(field.getModifiers()) && networkTypes.contains(field.getType().getName().replace('.', '/')))
                    return true;

        return false;
    }

    /**
     * Reads what a screen says: its title, and the first text its own class holds, such as why a connection attempt failed. Fields
     * are read in the order the class declares them, which HotSpot keeps.
     *
     * @return the text, or the screen's class name when it shows none
     */
    static String screenText(Object screen, String screenBaseName) {
        if (screen == null)
            return "no screen";

        Class<?> screenBase = screen.getClass().getSuperclass();

        while (screenBase != null && !screenBaseName.equals(screenBase.getName().replace('.', '/')))
            screenBase = screenBase.getSuperclass();

        String title = screenBase == null ? null : firstText(screen, screenBase);
        String text = firstText(screen, screen.getClass());

        if (title == null || title.equals(text))
            return text == null ? screen.getClass().getName() : text;

        return text == null ? title : title + ": " + text;
    }

    private static String firstText(Object screen, Class<?> type) {
        for (Field field : type.getDeclaredFields()) {
            if (Modifier.isStatic(field.getModifiers()) || field.getType().isPrimitive())
                continue;

            try {
                field.setAccessible(true);
                String text = JoinedConnection.text(field.get(screen));

                if (text != null && !text.trim().isEmpty())
                    return text;
            } catch (Throwable exception) {
                // A field that can't be read shows nothing.
            }
        }

        return null;
    }

    private static void awaitRoute(PendingOperation operation, String className) {
        if (operation.unroutableSince == 0L)
            operation.unroutableSince = System.currentTimeMillis();

        if (!className.equals(waitingScreenName))
        {
            waitingScreenName = className;
            System.err.println("Void client agent is waiting for a routable screen after " + className);
        }
    }

    /**
     * Keeps a connect from waiting forever on a screen without a route. After a short grace period, which lets screens that move on
     * by themselves do so, it opens the nearest screen on the route with the current screen as its parent, and after the deadline it
     * fails with the screen's name.
     */
    private static void checkRoute(GameAutomationIndex.IndexedCode index, PendingOperation operation, ClassLoader loader) throws Exception {
        if (operation.unroutableSince == 0L)
            return;

        long waited = System.currentTimeMillis() - operation.unroutableSince;
        Object screen = findCurrentScreen(screenSetterReceiver.get(), index.plan.screenBaseName);
        String screenName = screen == null ? "no screen" : screen.getClass().getName();

        if (waited >= RouteDeadlineMilliseconds) {
            complete(operation, error("ui.route", "No route to the direct connect screen from " + screenName));
            return;
        }

        if (waited < RouteGraceMilliseconds || operation.routeScreen != null || screen == null)
            return;

        Constructor<?> constructor = routeScreenConstructor(index, loader);

        if (constructor == null)
            return;

        operation.routeScreen = constructor.newInstance(screen);
        System.err.println("Void client warning: Screen " + screenName + " has no route to direct connect, opened "
            + constructor.getDeclaringClass().getName() + " with it as parent");
        Applying.set(Boolean.TRUE);

        try {
            setScreen(index.plan, operation.routeScreen);
        } finally {
            Applying.remove();
        }
    }

    /**
     * Finds the screen nearest to direct connect that opens with just a parent screen, such as the multiplayer screen. A mod menu
     * that doesn't extend a vanilla menu has no route of its own, but such a screen accepts it as its parent.
     *
     * @return the screen's one-parameter constructor, or {@code null} when no screen on the route has one
     */
    static Constructor<?> routeScreenConstructor(GameAutomationIndex.IndexedCode index, ClassLoader loader) {
        Class<?> screenBase;

        try {
            screenBase = loadClass(loader, index.plan.screenBaseName);
        } catch (ClassNotFoundException exception) {
            return null;
        }

        Constructor<?> nearest = null;
        int nearestDistance = Integer.MAX_VALUE;
        Set<String> seen = new HashSet<String>();

        for (List<TransitionPlan> transitions : index.plan.transitions.values())
            for (TransitionPlan transition : transitions)
                for (String target : transition.targetScreenNames) {
                    if (!seen.add(target))
                        continue;

                    int distance = index.plan.directScreenName.equals(target) ? 0 : distanceFrom(index, target, new HashSet<String>());

                    if (distance >= nearestDistance)
                        continue;

                    Constructor<?> constructor = parentConstructor(loader, target, screenBase);

                    if (constructor != null) {
                        nearest = constructor;
                        nearestDistance = distance;
                    }
                }

        return nearest;
    }

    private static Constructor<?> parentConstructor(ClassLoader loader, String className, Class<?> screenBase) {
        try {
            Class<?> type = loadClass(loader, className);

            if (Modifier.isAbstract(type.getModifiers()))
                return null;

            for (Constructor<?> constructor : type.getDeclaredConstructors()) {
                Class<?>[] parameters = constructor.getParameterTypes();

                if (parameters.length == 1 && parameters[0] == screenBase) {
                    constructor.setAccessible(true);
                    return constructor;
                }
            }
        } catch (Throwable exception) {
            // A screen that can't be loaded can't be opened either.
        }

        return null;
    }

    private static TransitionPlan selectTransition(GameAutomationIndex.IndexedCode index, List<TransitionPlan> transitions) {
        if (transitions.isEmpty())
            return null;

        List<TransitionPlan> candidates = new ArrayList<TransitionPlan>();
        int bestDistance = Integer.MAX_VALUE;

        for (TransitionPlan transition : transitions) {
            int distance = distanceToDirect(index, transition, new HashSet<String>());

            if (distance < bestDistance) {
                candidates.clear();
                candidates.add(transition);
                bestDistance = distance;
            } else if (distance == bestDistance) {
                candidates.add(transition);
            }
        }

        if (bestDistance == Integer.MAX_VALUE) {
            List<TransitionPlan> parentCandidates = new ArrayList<TransitionPlan>();

            for (TransitionPlan transition : transitions) {
                if (transition.targetsParent)
                    parentCandidates.add(transition);
            }

            if (parentCandidates.size() > 1) {
                List<TransitionPlan> enabling = new ArrayList<TransitionPlan>();

                for (TransitionPlan transition : parentCandidates) {
                    if (transition.enablesTransition)
                        enabling.add(transition);
                }

                parentCandidates = enabling;
            }

            return parentCandidates.size() == 1 ? parentCandidates.get(0) : null;
        }

        return candidates.size() == 1 ? candidates.get(0) : null;
    }

    private static int distanceToDirect(GameAutomationIndex.IndexedCode index, TransitionPlan transition, Set<String> visited) {
        int best = Integer.MAX_VALUE;

        for (String target : transition.targetScreenNames) {
            if (index.plan.directScreenName.equals(target))
                return 1;

            int nested = distanceFrom(index, target, visited);

            if (nested != Integer.MAX_VALUE)
                best = Math.min(best, nested + 1);
        }

        return best;
    }

    private static int distanceFrom(GameAutomationIndex.IndexedCode index, String source, Set<String> visited) {
        if (!visited.add(source))
            return Integer.MAX_VALUE;

        int best = Integer.MAX_VALUE;

        for (TransitionPlan transition : transitions(index, source))
            best = Math.min(best, distanceToDirect(index, transition, visited));

        visited.remove(source);
        return best;
    }

    /**
     * Finds the transitions of a live screen. A mod can replace a menu with its own subclass, which the index has never seen, so the
     * runtime superclasses lead to the menu whose routes still apply.
     */
    static List<TransitionPlan> transitions(GameAutomationIndex.IndexedCode index, Class<?> screenType) {
        for (Class<?> type = screenType; type != null; type = type.getSuperclass()) {
            List<TransitionPlan> transitions = transitions(index, type.getName().replace('.', '/'));

            if (!transitions.isEmpty())
                return transitions;
        }

        return Collections.emptyList();
    }

    private static List<TransitionPlan> transitions(GameAutomationIndex.IndexedCode index, String className) {
        String current = className;

        while (current != null) {
            List<TransitionPlan> transitions = index.plan.transitions.get(current);

            if (transitions != null)
                return transitions;

            current = index.superTypes.get(current);
        }

        return Collections.emptyList();
    }

    private static boolean invokeTransition(Object screen, TransitionPlan transition) throws Exception {
        ClassLoader loader = screen.getClass().getClassLoader();
        Class<?> owner = loadClass(loader, transition.owner);
        TypeArguments arguments = resolveArguments(loader, transition.methodDescriptor);
        Method method = MethodResolver.resolve(owner, transition.methodName, arguments.types);
        Object control = transition.controlId == null ? null : findControl(screen, transition, 4,
            Collections.newSetFromMap(new IdentityHashMap<Object, Boolean>()));

        if (transition.controlId != null && control == null)
            return false;

        for (int index = 0; index < arguments.values.length; index++) {
            Class<?> parameter = arguments.types[index];

            // An unused parameter has no bearing on the native action. Required
            // references must come from the live screen/control, never a guessed null.
            if (!transition.usedArguments[index])
                continue;
            if (parameter.isInstance(screen))
                arguments.values[index] = screen;
            else if (control != null && parameter.isInstance(control))
                arguments.values[index] = control;
            else if (parameter == Boolean.TYPE)
                arguments.values[index] = Boolean.TRUE;
            else if (parameter == Integer.TYPE && transition.controlId != null)
                arguments.values[index] = transition.controlId;
            else if (parameter != Boolean.TYPE)
                throw new IllegalStateException("No native argument binding for " + transition.describe() + " parameter " + index);
        }

        Object receiver = owner.isInstance(screen) ? screen : findCallback(screen, owner);
        if (!transition.isStatic && receiver == null)
            return false;
        method.invoke(transition.isStatic ? null : receiver, arguments.values);
        return true;
    }

    private static Object findCallback(Object screen, Class<?> owner) throws Exception {
        Set<Object> visited = Collections.newSetFromMap(new IdentityHashMap<Object, Boolean>());
        List<Object> remaining = new ArrayList<Object>();
        remaining.add(screen);
        Object found = null;
        for (int position = 0; position < remaining.size(); position++) {
            Object value = remaining.get(position);
            if (value == null || !visited.add(value))
                continue;
            if (owner.isInstance(value)) {
                if (found != null && found != value)
                    throw new IllegalStateException("Multiple live callbacks for " + owner.getName());
                found = value;
                continue;
            }
            if (value instanceof Collection<?>) {
                remaining.addAll((Collection<?>) value);
                continue;
            }
            if (value.getClass().isArray()) {
                if (!value.getClass().getComponentType().isPrimitive())
                    for (int index = 0; index < Array.getLength(value); index++)
                        remaining.add(Array.get(value, index));
                continue;
            }
            for (Class<?> type = value.getClass(); type != null && type != Object.class; type = type.getSuperclass())
                for (Field field : type.getDeclaredFields()) {
                    if (Modifier.isStatic(field.getModifiers()) || field.getType().isPrimitive())
                        continue;
                    if (!Collection.class.isAssignableFrom(field.getType()) && !field.getType().isArray()
                        && !field.getType().isAssignableFrom(owner))
                        continue;
                    field.setAccessible(true);
                    remaining.add(field.get(value));
                }
        }
        return found;
    }

    private static Object findControl(Object value, TransitionPlan transition, int depth, Set<Object> visited) throws Exception {
        if (value == null || depth < 0 || !visited.add(value))
            return null;

        if (isInstance(value, transition.controlClassName)) {
            Field id = declaredField(loadClass(value.getClass().getClassLoader(), transition.controlClassName), transition.controlIdFieldName);

            if (transition.controlId.equals(id.get(value)))
                return value;
        }

        if (value instanceof Collection<?>) {
            for (Object item : (Collection<?>) value) {
                Object result = findControl(item, transition, depth - 1, visited);

                if (result != null)
                    return result;
            }

            return null;
        }

        if (value.getClass().isArray()) {
            for (int index = 0; index < Array.getLength(value); index++) {
                Object result = findControl(Array.get(value, index), transition, depth - 1, visited);

                if (result != null)
                    return result;
            }

            return null;
        }

        for (Class<?> type = value.getClass(); type != null && type != Object.class; type = type.getSuperclass()) {
            for (Field field : type.getDeclaredFields()) {
                if (Modifier.isStatic(field.getModifiers()) || field.getType().isPrimitive()
                    || field.getType().getName().startsWith("java.lang."))
                    continue;

                field.setAccessible(true);
                Object result = findControl(field.get(value), transition, depth - 1, visited);

                if (result != null)
                    return result;
            }
        }

        return null;
    }

    private static Object constructChatScreen(ChatPlan plan, ClassLoader loader) throws Exception {
        TypeArguments arguments = resolveArguments(loader, plan.constructorDescriptor);
        Constructor<?> constructor = loadClass(loader, plan.screenClassName).getDeclaredConstructor(arguments.types);
        constructor.setAccessible(true);

        for (int index = 0; index < arguments.values.length; index++)
            arguments.values[index] = plan.constructorArguments[index];

        return constructor.newInstance(arguments.values);
    }

    private static void submitChat(Object screen, ChatPlan plan, String message) throws Exception {
        ClassLoader loader = screen.getClass().getClassLoader();
        Field textField = declaredField(screen.getClass(), plan.textFieldName);
        Object widget = textField.get(screen);
        Method setter = MethodResolver.resolve(loadClass(loader, plan.textSetterOwner), plan.textSetterName, String.class);
        setter.invoke(widget, message);
        Method getter = MethodResolver.resolve(loadClass(loader, plan.textGetterOwner), plan.textGetterName);
        String exact = (String) getter.invoke(widget);

        if (!message.equals(exact))
            throw new IllegalStateException("Minecraft returned " + quote(exact) + " instead of " + quote(message));

        TypeArguments arguments = resolveArguments(loader, plan.submitDescriptor);

        for (int index = 0; index < arguments.types.length; index++) {
            if (arguments.types[index] == String.class)
                arguments.values[index] = exact;
            else if (arguments.types[index] == Boolean.TYPE)
                arguments.values[index] = Boolean.TRUE;
        }

        Method submit = MethodResolver.resolve(loadClass(loader, plan.submitOwner), plan.submitName, arguments.types);
        submit.invoke(screen, arguments.values);
    }

    private static void setScreen(AutomationPlan plan, Object screen) throws Exception {
        Object receiver = screenSetterReceiver.get();

        if (receiver == null)
            throw new IllegalStateException("The Minecraft screen setter receiver is unavailable");

        ClassLoader loader = receiver.getClass().getClassLoader();
        Class<?> screenBase = loadClass(loader, plan.screenBaseName);
        Method setter = MethodResolver.resolve(loadClass(loader, plan.clientClassName), plan.screenSetterName, screenBase);
        setter.invoke(receiver, screen);
    }

    private static Object findObject(Object root, String internalName, int depth) {
        if (root == null || depth < 0)
            return null;

        if (isInstance(root, internalName))
            return root;

        Set<Object> visited = Collections.newSetFromMap(new IdentityHashMap<Object, Boolean>());
        List<Object> current = new ArrayList<Object>();
        current.add(root);

        for (int level = 0; level <= depth; level++) {
            List<Object> next = new ArrayList<Object>();

            for (Object value : current) {
                if (value == null || !visited.add(value))
                    continue;

                if (isInstance(value, internalName))
                    return value;

                for (Class<?> type = value.getClass(); type != null && type != Object.class; type = type.getSuperclass()) {
                    for (Field field : type.getDeclaredFields()) {
                        if (Modifier.isStatic(field.getModifiers()) || field.getType().isPrimitive())
                            continue;

                        try {
                            field.setAccessible(true);
                            next.add(field.get(value));
                        } catch (Throwable exception) {
                            // Inaccessible unrelated state is not part of the screen path.
                        }
                    }
                }
            }

            current = next;
        }

        return null;
    }

    private static boolean isCurrentScreen(Object client, Object screen, String screenBaseName) {
        return findCurrentScreen(client, screenBaseName) == screen;
    }

    private static Object findCurrentScreen(Object client, String screenBaseName) {
        if (client == null)
            return null;

        try {
            Class<?> screenBase = loadClass(client.getClass().getClassLoader(), screenBaseName);

            for (Class<?> type = client.getClass(); type != null && type != Object.class; type = type.getSuperclass()) {
                for (Field field : type.getDeclaredFields()) {
                    if (Modifier.isStatic(field.getModifiers()) || !screenBase.isAssignableFrom(field.getType()))
                        continue;

                    field.setAccessible(true);

                    Object value = field.get(client);

                    if (value != null)
                        return value;
                }
            }
        } catch (Throwable exception) {
            return null;
        }

        return null;
    }

    static boolean isPresentationOverlayActive(Object client, PresentationOverlayPlan overlay) throws Exception {
        if (client == null || overlay == null)
            return false;

        Field field = declaredField(loadClass(client.getClass().getClassLoader(), overlay.owner), overlay.fieldName);
        return field.get(client) != null;
    }

    private static GameAutomationIndex.IndexedCode index(ClassLoader loader) {
        return LoaderIndexes.get(loader);
    }

    private static boolean isInstance(Object value, String internalName) {
        if (value == null || internalName == null)
            return false;

        try {
            return loadClass(value.getClass().getClassLoader(), internalName).isInstance(value);
        } catch (Throwable exception) {
            return false;
        }
    }

    private static Class<?> loadClass(ClassLoader loader, String internalName) throws ClassNotFoundException {
        return Class.forName(internalName.replace('/', '.'), false, loader);
    }

    private static Field declaredField(Class<?> owner, String name) throws NoSuchFieldException {
        for (Class<?> type = owner; type != null; type = type.getSuperclass()) {
            try {
                Field field = type.getDeclaredField(name);
                field.setAccessible(true);
                return field;
            } catch (NoSuchFieldException exception) {
                // Continue through inherited screen fields.
            }
        }

        throw new NoSuchFieldException(owner.getName() + '.' + name);
    }

    private static TypeArguments resolveArguments(ClassLoader loader, String descriptor) throws ClassNotFoundException {
        Type[] argumentTypes = org.objectweb.asm.Type.getArgumentTypes(descriptor);
        Class<?>[] types = new Class<?>[argumentTypes.length];
        Object[] values = new Object[argumentTypes.length];

        for (int index = 0; index < argumentTypes.length; index++) {
            Type type = argumentTypes[index];

            switch (type.getSort()) {
                case Type.BOOLEAN:
                    types[index] = Boolean.TYPE;
                    values[index] = Boolean.FALSE;
                    break;
                case Type.BYTE:
                    types[index] = Byte.TYPE;
                    values[index] = Byte.valueOf((byte) 0);
                    break;
                case Type.CHAR:
                    types[index] = Character.TYPE;
                    values[index] = Character.valueOf((char) 0);
                    break;
                case Type.SHORT:
                    types[index] = Short.TYPE;
                    values[index] = Short.valueOf((short) 0);
                    break;
                case Type.INT:
                    types[index] = Integer.TYPE;
                    values[index] = Integer.valueOf(0);
                    break;
                case Type.LONG:
                    types[index] = Long.TYPE;
                    values[index] = Long.valueOf(0);
                    break;
                case Type.FLOAT:
                    types[index] = Float.TYPE;
                    values[index] = Float.valueOf(0);
                    break;
                case Type.DOUBLE:
                    types[index] = Double.TYPE;
                    values[index] = Double.valueOf(0);
                    break;
                case Type.ARRAY:
                    types[index] = Class.forName(type.getDescriptor().replace('/', '.'), false, loader);
                    break;
                default:
                    types[index] = loadClass(loader, type.getInternalName());
                    break;
            }
        }

        return new TypeArguments(types, values);
    }

    private static void fail(PendingOperation operation, String stage, Throwable exception,
                             GameAutomationIndex.IndexedCode index, Object screen) {
        while (exception instanceof java.lang.reflect.InvocationTargetException
            && ((java.lang.reflect.InvocationTargetException) exception).getTargetException() != null)
            exception = ((java.lang.reflect.InvocationTargetException) exception).getTargetException();

        synchronized (GameAutomationController.class) {
            if (pending == operation)
                pending = null;
        }

        exception.printStackTrace(System.err);
        String screenName = screen == null ? "none" : screen.getClass().getName();
        complete(operation, error(stage, exception.getClass().getName() + ": " + exception.getMessage()
            + "; screen=" + screenName + "; direct=" + index.plan.directScreenName));
    }

    private static synchronized void complete(PendingOperation operation, String response) {
        if (operation.response != null)
            return;
        if (pending == operation)
            pending = null;
        operation.response = response;
        operation.completed.countDown();
    }

    private static String error(String stage, String message) {
        return "{\"status\":\"unavailable\",\"stage\":" + quote(stage) + ",\"message\":" + quote(message) + "}";
    }

    private static String disconnected(String stage, String message) {
        return "{\"status\":\"disconnected\",\"stage\":" + quote(stage) + ",\"message\":" + quote(message) + "}";
    }

    private static String quote(String value) {
        if (value == null)
            return "null";

        StringBuilder result = new StringBuilder(value.length() + 2).append('"');

        for (int index = 0; index < value.length(); index++) {
            char character = value.charAt(index);

            if (character == '"' || character == '\\')
                result.append('\\').append(character);
            else if (character == '\n')
                result.append("\\n");
            else if (character == '\r')
                result.append("\\r");
            else if (character == '\t')
                result.append("\\t");
            else if (character < 0x20)
                result.append(String.format("\\u%04x", Integer.valueOf(character)));
            else
                result.append(character);
        }

        return result.append('"').toString();
    }

    private static final class PendingOperation {
        final String requestId;
        final String kind;
        final String value;
        final CountDownLatch completed = new CountDownLatch(1);
        volatile String response;
        volatile boolean submitted;
        volatile long submittedAt;
        volatile Object connectingScreen;
        volatile Object connection;
        volatile String rejection;
        volatile long closedAt;
        volatile long unroutableSince;
        volatile Object routeScreen;

        PendingOperation(String requestId, String kind, String value) {
            this.requestId = requestId;
            this.kind = kind;
            this.value = value;
        }
    }

    private static final class TypeArguments {
        final Class<?>[] types;
        final Object[] values;

        TypeArguments(Class<?>[] types, Object[] values) {
            this.types = types;
            this.values = values;
        }
    }
}

package voidclient.bootstrap;

import java.lang.invoke.MethodHandle;

/**
 * Receives the calls the agent injects into game and mod classes. The agent loads this class from the bootstrap class path because
 * mod loaders such as NeoForge 1.21.1 define those classes in loaders that can't see the agent jar, while every loader still
 * delegates to the bootstrap loader. It must therefore depend on nothing but JDK classes.
 */
public final class GameHooks {
    private static volatile MethodHandle playerRegistration;
    private static volatile MethodHandle clientFrame;
    private static volatile MethodHandle connectionListener;
    private static volatile MethodHandle connectionRejection;

    private GameHooks() {
    }

    public static void install(MethodHandle player, MethodHandle frame, MethodHandle listener, MethodHandle rejection) {
        playerRegistration = player;
        clientFrame = frame;
        connectionListener = listener;
        connectionRejection = rejection;
    }

    public static void registerPlayer(Object player) {
        MethodHandle target = playerRegistration;

        if (target == null)
            return;

        try {
            target.invokeExact(player);
        } catch (Throwable exception) {
            // The agent only observes the game, so its failures must never break the game code that called it.
        }
    }

    public static void applyClient(Object client) {
        MethodHandle target = clientFrame;

        if (target == null)
            return;

        try {
            target.invokeExact(client);
        } catch (Throwable exception) {
            // The agent only observes the game, so its failures must never break the game code that called it.
        }
    }

    public static void connectionListenerCreated(Object listener, Object connection) {
        MethodHandle target = connectionListener;

        if (target == null)
            return;

        try {
            target.invokeExact(listener, connection);
        } catch (Throwable exception) {
            // The agent only observes the game, so its failures must never break the game code that called it.
        }
    }

    public static void connectionRejected(Object listener, Object reason) {
        MethodHandle target = connectionRejection;

        if (target == null)
            return;

        try {
            target.invokeExact(listener, reason);
        } catch (Throwable exception) {
            // The agent only observes the game, so its failures must never break the game code that called it.
        }
    }
}

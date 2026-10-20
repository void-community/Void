package voidclient.agent;

import java.lang.ref.WeakReference;
import java.lang.reflect.Field;
import java.lang.reflect.Method;
import java.lang.reflect.Modifier;

/**
 * Follows the network connection of the server the game last joined. One connection carries login, configuration, play and a
 * proxy's server switches, so it stays open until the game really leaves that server.
 */
final class JoinedConnection {
    private static final String ChannelName = "io.netty.channel.Channel";
    private static final String MessageName = "com.mojang.brigadier.Message";
    private static volatile WeakReference<Object> connection = new WeakReference<Object>(null);
    private static volatile boolean joined;
    private static volatile String disconnectReason;

    private JoinedConnection() {
    }

    static void joined(Object value) {
        disconnectReason = null;
        connection = new WeakReference<Object>(value);
        joined = value != null;
    }

    static void disconnected(Object value, Object reason) {
        if (value != null && value == connection.get())
            disconnectReason = reasonText(reason);
    }

    /**
     * Reads the plain text of a disconnect reason. Text components implement Brigadier's {@code Message}, whose method keeps its name
     * in obfuscated builds, and newer versions wrap the component in a record with the report details.
     *
     * @return the text, or the reason's own description when it has none
     */
    static String reasonText(Object reason) {
        String text = text(reason);

        // Versions before Brigadier, and reasons that aren't text components, keep their own description.
        return text == null ? String.valueOf(reason) : text;
    }

    /**
     * Reads the plain text of a string, a text component, or a record holding a text component.
     *
     * @return the text, or {@code null} when the value isn't text
     */
    static String text(Object value) {
        if (value instanceof String)
            return (String) value;

        if (value == null)
            return null;

        try {
            Class<?> message = Class.forName(MessageName, false, value.getClass().getClassLoader());
            Object text = message.isInstance(value) ? value : null;

            if (text == null && value.getClass().getSuperclass() != null && "java.lang.Record".equals(value.getClass().getSuperclass().getName()))
                for (Object component : (Object[]) Class.class.getMethod("getRecordComponents").invoke(value.getClass())) {
                    Method accessor = (Method) component.getClass().getMethod("getAccessor").invoke(component);
                    accessor.setAccessible(true);
                    Object componentValue = accessor.invoke(value);

                    if (message.isInstance(componentValue)) {
                        text = componentValue;
                        break;
                    }
                }

            if (text != null)
                return (String) message.getMethod("getString").invoke(text);
        } catch (Exception exception) {
            // Versions before Brigadier have no text components to read.
        }

        return null;
    }

    static boolean hasJoined() {
        return joined;
    }

    /**
     * Tells why the game left the joined server.
     *
     * @return the disconnect reason, or {@code null} while the connection is open or when no server was joined
     */
    static String closedReason() {
        if (!joined)
            return null;

        Object value = connection.get();
        Boolean open = value == null ? Boolean.FALSE : isOpen(value);
        String reason = disconnectReason;

        // The channel decides when it can be read, because a disconnect callback can only add the reason.
        if (Boolean.TRUE.equals(open) || open == null && reason == null)
            return null;

        return reason == null ? "The connection to the server closed" : reason;
    }

    /**
     * Reads whether the netty channel of a Minecraft connection is open.
     *
     * @return the channel state, or {@code null} when the connection has no channel yet or it cannot be read
     */
    static Boolean isOpen(Object value) {
        if (value == null)
            return null;

        for (Class<?> type = value.getClass(); type != null; type = type.getSuperclass())
            for (Field field : type.getDeclaredFields()) {
                if (Modifier.isStatic(field.getModifiers()) || !ChannelName.equals(field.getType().getName()))
                    continue;

                try {
                    field.setAccessible(true);
                    Object channel = field.get(value);
                    return channel == null ? null : (Boolean) field.getType().getMethod("isOpen").invoke(channel);
                } catch (Exception exception) {
                    return null;
                }
            }

        return null;
    }
}

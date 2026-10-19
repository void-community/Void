package voidclient.agent;

import java.lang.reflect.Array;
import java.lang.reflect.Field;
import java.lang.reflect.Method;
import java.lang.reflect.Modifier;
import java.util.ArrayList;
import java.util.Collection;
import java.util.Collections;
import java.util.IdentityHashMap;
import java.util.List;
import java.util.Map;
import java.util.Set;
import org.objectweb.asm.Opcodes;
import org.objectweb.asm.tree.AbstractInsnNode;
import org.objectweb.asm.tree.ClassNode;
import org.objectweb.asm.tree.FieldInsnNode;
import org.objectweb.asm.tree.LdcInsnNode;
import org.objectweb.asm.tree.MethodNode;

/**
 * Presses the Proceed button of screens the UI graph can't route through, such as warnings mods show before the title screen.
 * Minecraft labels those buttons with a shared component whose translation key, unlike class and member names, stays the same in
 * obfuscated, Mojang-mapped, and remapped game code.
 */
final class ProceedButtons {
    static final String TranslationKey = "gui.proceed";

    private ProceedButtons() {
    }

    /** Finds the static fields that game code initializes with the translatable Proceed component. */
    static List<String[]> discover(Map<String, ClassNode> types) {
        List<String[]> labels = new ArrayList<String[]>();

        for (ClassNode type : types.values()) {
            for (MethodNode method : type.methods) {
                if (!"<clinit>".equals(method.name))
                    continue;

                for (AbstractInsnNode instruction = method.instructions.getFirst(); instruction != null; instruction = instruction.getNext()) {
                    if (!(instruction instanceof LdcInsnNode) || !TranslationKey.equals(((LdcInsnNode) instruction).cst))
                        continue;

                    AbstractInsnNode translate = next(instruction);
                    AbstractInsnNode store = translate == null ? null : next(translate);

                    if (translate == null || translate.getOpcode() != Opcodes.INVOKESTATIC || store == null || store.getOpcode() != Opcodes.PUTSTATIC)
                        continue;

                    FieldInsnNode field = (FieldInsnNode) store;

                    if (field.owner.equals(type.name))
                        labels.add(new String[] { field.owner, field.name });
                }
            }
        }

        return labels;
    }

    /**
     * Presses the button labelled with one of the discovered Proceed components through its press callback.
     *
     * @return the pressed button, or {@code null} when the screen has no single such button
     */
    static Object press(Object screen, List<String[]> labels) throws Exception {
        Set<Object> labelValues = Collections.newSetFromMap(new IdentityHashMap<Object, Boolean>());

        for (String[] label : labels) {
            Field field = Class.forName(label[0].replace('/', '.'), false, screen.getClass().getClassLoader()).getDeclaredField(label[1]);
            field.setAccessible(true);
            Object value = field.get(null);

            if (value != null)
                labelValues.add(value);
        }

        if (labelValues.isEmpty())
            return null;

        Set<Object> buttons = findButtons(screen, labelValues);

        if (buttons.size() != 1)
            return null;

        Object button = buttons.iterator().next();
        Object[] callback = findPressCallback(button);

        if (callback == null)
            return null;

        ((Method) callback[1]).invoke(callback[0], button);
        return button;
    }

    /** Looks at the objects the screen holds directly or in its widget lists, which is where screens keep their buttons. */
    private static Set<Object> findButtons(Object screen, Set<Object> labelValues) throws Exception {
        Set<Object> buttons = Collections.newSetFromMap(new IdentityHashMap<Object, Boolean>());

        for (Object value : fieldValues(screen)) {
            if (value instanceof Collection<?>) {
                for (Object item : (Collection<?>) value)
                    addButton(item, screen, labelValues, buttons);
            } else if (value.getClass().isArray() && !value.getClass().getComponentType().isPrimitive()) {
                for (int index = 0; index < Array.getLength(value); index++)
                    addButton(Array.get(value, index), screen, labelValues, buttons);
            } else {
                addButton(value, screen, labelValues, buttons);
            }
        }

        return buttons;
    }

    private static void addButton(Object candidate, Object screen, Set<Object> labelValues, Set<Object> buttons) throws Exception {
        if (candidate == null || candidate == screen || buttons.contains(candidate))
            return;

        // JDK objects are never buttons, and their private fields are closed to reflection on newer Java versions.
        ClassLoader loader = candidate.getClass().getClassLoader();

        if (loader == null || loader == ClassLoader.getSystemClassLoader().getParent())
            return;

        for (Object value : fieldValues(candidate)) {
            if (labelValues.contains(value) && findPressCallback(candidate) != null) {
                buttons.add(candidate);
                return;
            }
        }
    }

    private static List<Object> fieldValues(Object value) throws Exception {
        List<Object> values = new ArrayList<Object>();

        for (Class<?> type = value.getClass(); type != null && type != Object.class; type = type.getSuperclass()) {
            for (Field field : type.getDeclaredFields()) {
                if (Modifier.isStatic(field.getModifiers()) || field.getType().isPrimitive())
                    continue;

                try {
                    field.setAccessible(true);
                } catch (RuntimeException inaccessible) {
                    continue;
                }

                Object fieldValue = field.get(value);

                if (fieldValue != null)
                    values.add(fieldValue);
            }
        }

        return values;
    }

    /** Finds the button's callback: a field holding a single-method interface that takes the button itself. */
    private static Object[] findPressCallback(Object button) throws Exception {
        Object[] found = null;

        for (Class<?> type = button.getClass(); type != null && type != Object.class; type = type.getSuperclass()) {
            for (Field field : type.getDeclaredFields()) {
                if (Modifier.isStatic(field.getModifiers()) || !field.getType().isInterface())
                    continue;

                Method method = singleAbstractMethod(field.getType());

                if (method == null || method.getParameterTypes().length != 1 || !method.getParameterTypes()[0].isInstance(button))
                    continue;

                field.setAccessible(true);
                Object callback = field.get(button);

                if (callback == null)
                    continue;

                if (found != null)
                    return null;

                method.setAccessible(true);
                found = new Object[] { callback, method };
            }
        }

        return found;
    }

    private static Method singleAbstractMethod(Class<?> type) {
        Method found = null;

        for (Method method : type.getMethods()) {
            if (!Modifier.isAbstract(method.getModifiers()))
                continue;

            if (found != null)
                return null;

            found = method;
        }

        return found;
    }

    private static AbstractInsnNode next(AbstractInsnNode instruction) {
        AbstractInsnNode next = instruction.getNext();

        while (next != null && next.getOpcode() < 0)
            next = next.getNext();

        return next;
    }
}

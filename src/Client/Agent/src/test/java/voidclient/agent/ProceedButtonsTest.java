package voidclient.agent;

import java.util.Arrays;
import java.util.Collections;
import java.util.HashMap;
import java.util.List;
import java.util.Map;
import org.junit.Assert;
import org.junit.Test;
import org.objectweb.asm.Opcodes;
import org.objectweb.asm.tree.ClassNode;
import org.objectweb.asm.tree.FieldInsnNode;
import org.objectweb.asm.tree.InsnNode;
import org.objectweb.asm.tree.LdcInsnNode;
import org.objectweb.asm.tree.MethodInsnNode;
import org.objectweb.asm.tree.MethodNode;

public final class ProceedButtonsTest {
    private static final List<String[]> Labels = Collections.singletonList(new String[] { Label.class.getName().replace('.', '/'), "PROCEED" });

    @Test
    public void discoversStaticFieldsInitializedWithTheProceedTranslationKey() {
        Map<String, ClassNode> types = new HashMap<String, ClassNode>();
        types.put("game/CommonComponents", componentHolder("game/CommonComponents", "a", ProceedButtons.TranslationKey));
        types.put("game/Other", componentHolder("game/Other", "b", "gui.cancel"));

        List<String[]> labels = ProceedButtons.discover(types);

        Assert.assertEquals(1, labels.size());
        Assert.assertArrayEquals(new String[] { "game/CommonComponents", "a" }, labels.get(0));
    }

    @Test
    public void pressesTheProceedButtonOfAScreen() throws Exception {
        Button proceed = new Button(Label.PROCEED);
        Button guide = new Button(Label.GUIDE);
        Screen screen = new Screen(proceed, guide);

        Assert.assertSame(proceed, ProceedButtons.press(screen, Labels));
        Assert.assertEquals(1, proceed.presses);
        Assert.assertEquals(0, guide.presses);
    }

    @Test
    public void leavesScreensWithoutASingleProceedButtonAlone() throws Exception {
        Button first = new Button(Label.PROCEED);
        Button second = new Button(Label.PROCEED);

        Assert.assertNull(ProceedButtons.press(new Screen(new Button(Label.GUIDE)), Labels));
        Assert.assertNull(ProceedButtons.press(new Screen(first, second), Labels));
        Assert.assertEquals(0, first.presses + second.presses);
    }

    private static ClassNode componentHolder(String name, String fieldName, String translationKey) {
        ClassNode type = new ClassNode();
        type.name = name;
        MethodNode initializer = new MethodNode(Opcodes.ACC_STATIC, "<clinit>", "()V", null, null);
        initializer.instructions.add(new LdcInsnNode(translationKey));
        initializer.instructions.add(new MethodInsnNode(Opcodes.INVOKESTATIC, "game/Component", "translatable", "(Ljava/lang/String;)Lgame/Component;", true));
        initializer.instructions.add(new FieldInsnNode(Opcodes.PUTSTATIC, name, fieldName, "Lgame/Component;"));
        initializer.instructions.add(new InsnNode(Opcodes.RETURN));
        type.methods.add(initializer);
        return type;
    }

    public static final class Label {
        static final Label PROCEED = new Label();
        static final Label GUIDE = new Label();
    }

    public interface OnPress {
        void onPress(Button button);
    }

    public static final class Button {
        private final Label message;
        private final OnPress onPress = new OnPress() {
            @Override
            public void onPress(Button button) {
                button.presses++;
            }
        };
        int presses;

        Button(Label message) {
            this.message = message;
        }
    }

    public static final class Screen {
        private final Label title = Label.GUIDE;
        private final List<Button> children;

        Screen(Button... buttons) {
            children = Arrays.asList(buttons);
        }
    }
}

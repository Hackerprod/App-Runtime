package org.example.uiprobe;

import android.app.Activity;
import android.os.Bundle;
import android.view.View;
import android.widget.TextView;
import android.widget.Toast;

public final class MainActivity extends Activity {
    public int observedId;
    public boolean stateRoundTrip;
    public String observedText;

    @Override public void onCreate(Bundle state) {
        super.onCreate(state);
        setContentView(R.layout.main);
        TextView label = (TextView) findViewById(R.id.label);
        label.setText("Ready");
        observedText = label.getText().toString();
        View action = findViewById(R.id.action);
        observedId = action.findViewById(R.id.action).getId();
        action.setEnabled(false);
        boolean disabled = !action.isEnabled();
        action.setEnabled(true);
        action.setVisibility(View.INVISIBLE);
        boolean invisible = action.getVisibility() == View.INVISIBLE;
        action.setVisibility(View.VISIBLE);
        action.setOnClickListener(null);
        stateRoundTrip = disabled && invisible && action.isEnabled();
    }

    public void handleClick(View view) {
        TextView label = (TextView) findViewById(R.id.label);
        label.setText("Clicked");
        Toast.makeText(this, "Clicked", Toast.LENGTH_SHORT).show();
    }
}
